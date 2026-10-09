using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Cypher
{
    /// <summary>
    /// All mouse handling in one place, as a small state machine, so the rules can't conflict:
    ///  - Press on empty space, then drag: turns the camera (left/right and up/down).
    ///    Double-click empty space: back to the default view.
    ///  - Press on a hologram, then move past a few pixels: drags the hologram (camera stays still).
    ///  - Press and release on a hologram without moving: a click. Two clicks in a row = edit.
    ///    Any drag cancels a pending double-click, so dragging can never open the editor.
    ///  - Click the pencil icon: edit.
    ///  - Scroll while dragging: push the hologram away / pull it closer.
    /// </summary>
    public class InteractionController : MonoBehaviour
    {
        [SerializeField] Camera cam;
        [SerializeField] CameraRigController cameraRig;

        [Header("Clicks")]
        [Tooltip("How far (pixels) the mouse must move while pressed before it counts as a drag.")]
        [SerializeField] float dragThresholdPixels = 6f;
        [SerializeField] float doubleClickSeconds = 0.35f;
        [SerializeField] float doubleClickMaxPixels = 10f;

        [Header("Dragging")]
        [Tooltip("Meters per scroll unit when pushing/pulling a dragged hologram.")]
        [SerializeField] float scrollDepthSpeed = 0.002f;
        [SerializeField] float minDragDistance = 0.45f;
        [SerializeField] float maxDragDistance = 3f;
        [SerializeField] float minHologramHeight = 0.8f;

        [SerializeField] LayerMask raycastMask = ~0;

        enum State { Idle, PressedHologram, DraggingHologram, RotatingCamera }

        State state;
        HologramPanel hovered;
        bool hoveringPencil;

        HologramPanel pressed;
        bool pressedPencil;
        Vector2 pressScreenPos;

        Vector3 grabOffset;
        float dragDistance;

        HologramPanel lastClickPanel;
        float lastClickTime;
        Vector2 lastClickPos;
        float lastEmptyClickTime = -1f; // double-click on empty space = recenter the view

        public HologramPanel Hovered => hovered;

        void Reset() => cam = Camera.main;

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || cam == null) return;

            if (InputLock.IsLocked)
            {
                CancelEverything();
                return;
            }

            Vector2 mousePos = mouse.position.ReadValue();
            bool held = mouse.leftButton.isPressed;

            switch (state)
            {
                case State.Idle:
                    // Over a UI button (desk IMPORT button, prompts): let the UI have the click.
                    if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    {
                        if (hovered != null) hovered.SetHovered(false);
                        hovered = null;
                        break;
                    }
                    UpdateHover(mousePos);
                    if (mouse.leftButton.wasPressedThisFrame)
                    {
                        if (hovered != null)
                        {
                            state = State.PressedHologram;
                            pressed = hovered;
                            pressedPencil = hoveringPencil;
                            pressScreenPos = mousePos;
                        }
                        else
                        {
                            state = State.RotatingCamera;
                            pressScreenPos = mousePos;
                        }
                    }
                    break;

                case State.PressedHologram:
                    if (pressed == null || !pressed.IsInteractable)
                    {
                        pressed = null;
                        state = State.Idle;
                    }
                    else if (!held)
                    {
                        HandleClick(pressed, pressedPencil, mousePos);
                        pressed = null;
                        state = State.Idle;
                    }
                    else if ((mousePos - pressScreenPos).sqrMagnitude > dragThresholdPixels * dragThresholdPixels)
                    {
                        BeginDrag();
                    }
                    break;

                case State.DraggingHologram:
                    if (pressed == null || !pressed.IsInteractable)
                    {
                        // Deleted mid-drag: let go without saving a position for it.
                        if (pressed != null) pressed.EndDrag();
                        pressed = null;
                        state = State.Idle;
                    }
                    else if (!held) EndDrag();
                    else UpdateDrag(mousePos, mouse.scroll.ReadValue().y);
                    break;

                case State.RotatingCamera:
                    if (!held)
                    {
                        state = State.Idle;
                        bool wasClick = (mousePos - pressScreenPos).sqrMagnitude <= dragThresholdPixels * dragThresholdPixels;
                        if (wasClick && cameraRig != null)
                        {
                            if (Time.unscaledTime - lastEmptyClickTime <= doubleClickSeconds)
                            {
                                cameraRig.Recenter();
                                lastEmptyClickTime = -1f;
                            }
                            else lastEmptyClickTime = Time.unscaledTime;
                        }
                    }
                    else if (cameraRig != null) cameraRig.AddDragDelta(mouse.delta.ReadValue());
                    break;
            }
        }

        void UpdateHover(Vector2 mousePos)
        {
            HologramPanel hit = null;
            bool pencil = false;

            var ray = cam.ScreenPointToRay(mousePos);
            if (Physics.Raycast(ray, out var info, 50f, raycastMask, QueryTriggerInteraction.Collide))
            {
                var pencilComp = info.collider.GetComponent<HologramPencil>();
                pencil = pencilComp != null;
                hit = pencil ? pencilComp.Owner : info.collider.GetComponentInParent<HologramPanel>();
                if (hit != null && !hit.IsInteractable) hit = null; // still building in, or on its way out
            }

            hoveringPencil = pencil;
            if (hit == hovered) return;
            if (hovered != null) hovered.SetHovered(false);
            hovered = hit;
            if (hovered != null)
            {
                hovered.SetHovered(true);
                CypherAudio.Play(Sfx.Hover, hovered.transform.position);
            }
        }

        void HandleClick(HologramPanel panel, bool onPencil, Vector2 mousePos)
        {
            if (panel == null) return;
            CypherAudio.Play(Sfx.Click, panel.transform.position);

            if (onPencil)
            {
                lastClickPanel = null;
                TaskManager.Instance.RequestEdit(panel);
                return;
            }

            bool isDoubleClick = panel == lastClickPanel
                && Time.unscaledTime - lastClickTime <= doubleClickSeconds
                && (mousePos - lastClickPos).sqrMagnitude <= doubleClickMaxPixels * doubleClickMaxPixels;

            if (isDoubleClick)
            {
                lastClickPanel = null;
                TaskManager.Instance.RequestEdit(panel);
            }
            else
            {
                lastClickPanel = panel;
                lastClickTime = Time.unscaledTime;
                lastClickPos = mousePos;
            }
        }

        void BeginDrag()
        {
            state = State.DraggingHologram;
            lastClickPanel = null; // a drag must never become half of a double-click

            Vector3 forward = cam.transform.forward;
            Vector3 panelPos = pressed.transform.position;
            dragDistance = Vector3.Dot(panelPos - cam.transform.position, forward);

            // Remember where on the panel we grabbed it, so it doesn't jump to center on the cursor.
            var plane = new Plane(-forward, panelPos);
            var ray = cam.ScreenPointToRay(pressScreenPos);
            grabOffset = plane.Raycast(ray, out float enter) ? panelPos - ray.GetPoint(enter) : Vector3.zero;

            pressed.BeginDrag();
            CypherAudio.Play(Sfx.PickUp, pressed.transform.position);
        }

        void UpdateDrag(Vector2 mousePos, float scroll)
        {
            dragDistance = Mathf.Clamp(dragDistance + scroll * scrollDepthSpeed, minDragDistance, maxDragDistance);

            Vector3 forward = cam.transform.forward;
            var plane = new Plane(-forward, cam.transform.position + forward * dragDistance);
            var ray = cam.ScreenPointToRay(mousePos);
            if (!plane.Raycast(ray, out float enter)) return;

            Vector3 target = ray.GetPoint(enter) + grabOffset;
            target.y = Mathf.Max(target.y, minHologramHeight);
            pressed.DragTo(target);
        }

        void EndDrag()
        {
            pressed.EndDrag();
            CypherAudio.Play(Sfx.Drop, pressed.transform.position);
            TaskManager.Instance.OnHologramDragEnded(pressed);
            pressed = null;
            state = State.Idle;
        }

        void CancelEverything()
        {
            if (state == State.DraggingHologram && pressed != null) EndDrag();
            if (hovered != null) hovered.SetHovered(false);
            hovered = null;
            pressed = null;
            lastClickPanel = null;
            state = State.Idle;
        }
    }
}
