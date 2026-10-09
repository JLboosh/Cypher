using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// Google sign-in for a desktop app (OAuth 2.0 "installed app" flow):
    ///   1. listen on http://127.0.0.1:&lt;random port&gt; (loopback redirect),
    ///   2. open the Google consent page in your browser (with PKCE, so the code can't be reused),
    ///   3. receive the code on the loopback port, exchange it for tokens,
    ///   4. keep the refresh token in ~/Library/Application Support/.../google-token.json.
    /// Access tokens are refreshed automatically.
    /// </summary>
    public class GoogleAuth
    {
        const string AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
        const string TokenUrl = "https://oauth2.googleapis.com/token";
        const string RevokeUrl = "https://oauth2.googleapis.com/revoke";

        static readonly string[] Scopes =
        {
            "https://www.googleapis.com/auth/calendar.calendarlist.readonly", // list your calendars
            "https://www.googleapis.com/auth/calendar.events",                // read events, sync edits back
            "https://www.googleapis.com/auth/tasks",                          // read/update Google Tasks
        };

        [Serializable]
        class TokenFile
        {
            public string access_token;
            public string refresh_token;
            public long expires_at; // unix seconds
        }

        [Serializable]
        class TokenResponse
        {
            public string access_token;
            public string refresh_token;
            public int expires_in;
            public string error;
            public string error_description;
        }

        readonly CypherConfig config;
        TokenFile token;

        static string TokenPath => Path.Combine(Application.persistentDataPath, "google-token.json");

        public GoogleAuth(CypherConfig config)
        {
            this.config = config;
            try
            {
                if (File.Exists(TokenPath)) token = JsonUtility.FromJson<TokenFile>(File.ReadAllText(TokenPath));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Cypher] Couldn't read the Google token file: " + e.Message);
            }
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(config.googleClientId) && !string.IsNullOrWhiteSpace(config.googleClientSecret);
        public bool IsConnected => token != null && !string.IsNullOrEmpty(token.refresh_token);

        /// <summary>Opens the browser for sign-in and waits (up to 3 minutes) for you to approve.</summary>
        public async Task<string> Connect()
        {
            if (!IsConfigured) return "Add googleClientId and googleClientSecret to the config file first.";

            string verifier = RandomUrlSafe(48);
            string challenge = Base64Url(SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            string state = RandomUrlSafe(16);

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string redirect = $"http://127.0.0.1:{port}";

            string url = AuthUrl
                + "?client_id=" + Uri.EscapeDataString(config.googleClientId.Trim())
                + "&redirect_uri=" + Uri.EscapeDataString(redirect)
                + "&response_type=code"
                + "&scope=" + Uri.EscapeDataString(string.Join(" ", Scopes))
                + "&code_challenge=" + challenge
                + "&code_challenge_method=S256"
                + "&state=" + state
                + "&access_type=offline&prompt=consent";
            Application.OpenURL(url);

            string code;
            try { code = await Task.Run(() => WaitForCode(listener, state, TimeSpan.FromMinutes(3))); }
            finally { listener.Stop(); }
            if (code == null) return "Sign-in was cancelled or timed out.";

            var r = await Http.PostForm(TokenUrl, new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = config.googleClientId.Trim(),
                ["client_secret"] = config.googleClientSecret.Trim(),
                ["redirect_uri"] = redirect,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = verifier,
            });
            var parsed = JsonUtility.FromJson<TokenResponse>(r.Text ?? "{}");
            if (!r.Ok || string.IsNullOrEmpty(parsed.access_token))
            {
                Http.LogFailure("Google token exchange", r);
                return "Google sign-in failed: " + (parsed.error_description ?? parsed.error ?? r.Error);
            }

            token = new TokenFile
            {
                access_token = parsed.access_token,
                refresh_token = parsed.refresh_token,
                expires_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + parsed.expires_in,
            };
            SaveToken();
            return null; // success
        }

        /// <summary>A valid access token (refreshed if needed), or null if not connected.</summary>
        public async Task<string> GetAccessToken()
        {
            if (!IsConnected) return null;
            if (token.expires_at - 60 > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return token.access_token;

            var r = await Http.PostForm(TokenUrl, new Dictionary<string, string>
            {
                ["client_id"] = config.googleClientId.Trim(),
                ["client_secret"] = config.googleClientSecret.Trim(),
                ["refresh_token"] = token.refresh_token,
                ["grant_type"] = "refresh_token",
            });
            var parsed = JsonUtility.FromJson<TokenResponse>(r.Text ?? "{}");
            if (!r.Ok || string.IsNullOrEmpty(parsed.access_token))
            {
                Http.LogFailure("Google token refresh", r);
                if (parsed.error == "invalid_grant") Disconnect(revoke: false); // expired/revoked: sign in again
                return null;
            }
            token.access_token = parsed.access_token;
            token.expires_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + parsed.expires_in;
            SaveToken();
            return token.access_token;
        }

        public async void Disconnect(bool revoke = true)
        {
            var old = token;
            token = null;
            try { if (File.Exists(TokenPath)) File.Delete(TokenPath); } catch { /* ignore */ }
            if (revoke && old != null && !string.IsNullOrEmpty(old.refresh_token))
                await Http.PostForm(RevokeUrl, new Dictionary<string, string> { ["token"] = old.refresh_token });
        }

        void SaveToken() => File.WriteAllText(TokenPath, JsonUtility.ToJson(token));

        /// <summary>Runs on a worker thread: accepts the browser's redirect and pulls out ?code=.</summary>
        static string WaitForCode(TcpListener listener, string expectedState, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (!listener.Pending())
                {
                    System.Threading.Thread.Sleep(100);
                    continue;
                }
                using (var client = listener.AcceptTcpClient())
                using (var stream = client.GetStream())
                {
                    var reader = new StreamReader(stream, Encoding.ASCII);
                    string requestLine = reader.ReadLine() ?? "";      // GET /?code=...&state=... HTTP/1.1
                    var query = ParseQuery(requestLine);
                    bool ok = query.TryGetValue("code", out var code) && query.TryGetValue("state", out var st) && st == expectedState;
                    bool denied = query.ContainsKey("error");

                    string body = ok
                        ? "<h2 style='font-family:sans-serif'>Cypher is connected to Google.</h2><p style='font-family:sans-serif'>You can close this tab.</p>"
                        : denied ? "<h2 style='font-family:sans-serif'>Sign-in cancelled.</h2>" : "";
                    string status = ok || denied ? "200 OK" : "404 Not Found"; // e.g. /favicon.ico
                    byte[] bytes = Encoding.UTF8.GetBytes(
                        $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
                    stream.Write(bytes, 0, bytes.Length);

                    if (ok) return code;
                    if (denied) return null;
                }
            }
            return null;
        }

        static Dictionary<string, string> ParseQuery(string requestLine)
        {
            var result = new Dictionary<string, string>();
            var parts = requestLine.Split(' ');
            if (parts.Length < 2) return result;
            int q = parts[1].IndexOf('?');
            if (q < 0) return result;
            foreach (var pair in parts[1].Substring(q + 1).Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0) result[Uri.UnescapeDataString(pair.Substring(0, eq))] = Uri.UnescapeDataString(pair.Substring(eq + 1));
            }
            return result;
        }

        static string RandomUrlSafe(int bytes)
        {
            var data = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(data);
            return Base64Url(data);
        }

        static string Base64Url(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
