using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeExplainer
{
    internal sealed class GoogleSignInCoordinator
    {
        private readonly ClientConfig _config;

        public GoogleSignInCoordinator(ClientConfig config)
        {
            _config = config;
        }

        public async Task<GoogleAuthorizationResult> AuthorizeAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_config.GoogleClientId))
            {
                throw new GoogleSignInException("Google sign-in is not configured for this build.");
            }

            string redirectUri = BuildRedirectUri(_config.GoogleRedirectPort);
            string state = CreateRandomBase64Url(24);
            string codeVerifier = CreateRandomBase64Url(64);
            string codeChallenge = CreateCodeChallenge(codeVerifier);

            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);

            try
            {
                listener.Start();
            }
            catch (Exception ex)
            {
                throw new GoogleSignInException("Unable to start the local Google sign-in callback listener.", ex);
            }

            try
            {
                string authUrl = BuildAuthorizationUrl(_config.GoogleClientId, redirectUri, state, codeChallenge);
                try
                {
                    Process.Start(new ProcessStartInfo(authUrl)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    throw new GoogleSignInException("Unable to open the browser for Google sign-in.", ex);
                }

                HttpListenerContext context = await WaitForCallbackAsync(listener, cancellationToken);
                try
                {
                    string returnedState = context.Request.QueryString["state"]?.Trim() ?? string.Empty;
                    string error = context.Request.QueryString["error"]?.Trim() ?? string.Empty;
                    string code = context.Request.QueryString["code"]?.Trim() ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        await WriteBrowserResponseAsync(context.Response, false, "Sign-in canceled", "You can close this window and return to simpleDocs.");
                        throw new GoogleSignInException(MapGoogleError(error));
                    }

                    if (!string.Equals(returnedState, state, StringComparison.Ordinal))
                    {
                        await WriteBrowserResponseAsync(context.Response, false, "Sign-in failed", "The sign-in response did not match this app session.");
                        throw new GoogleSignInException("Google sign-in returned an invalid state token.");
                    }

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        await WriteBrowserResponseAsync(context.Response, false, "Sign-in failed", "Google did not return an authorization code.");
                        throw new GoogleSignInException("Google sign-in did not return an authorization code.");
                    }

                    await WriteBrowserResponseAsync(context.Response, true, "Sign-in complete", "You can close this window and return to simpleDocs.");

                    return new GoogleAuthorizationResult
                    {
                        AuthorizationCode = code,
                        CodeVerifier = codeVerifier,
                        RedirectUri = redirectUri
                    };
                }
                finally
                {
                    context.Response.Close();
                }
            }
            finally
            {
                if (listener.IsListening)
                {
                    listener.Stop();
                }
            }
        }

        private static string BuildRedirectUri(int port)
        {
            int safePort = port > 0 ? port : 48152;
            return $"http://127.0.0.1:{safePort}/";
        }

        private static string BuildAuthorizationUrl(string clientId, string redirectUri, string state, string codeChallenge)
        {
            string scope = Uri.EscapeDataString("openid email profile");
            return
                "https://accounts.google.com/o/oauth2/v2/auth" +
                $"?client_id={Uri.EscapeDataString(clientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                "&response_type=code" +
                $"&scope={scope}" +
                $"&state={Uri.EscapeDataString(state)}" +
                $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                "&code_challenge_method=S256" +
                "&prompt=select_account";
        }

        private static async Task<HttpListenerContext> WaitForCallbackAsync(HttpListener listener, CancellationToken cancellationToken)
        {
            Task<HttpListenerContext> contextTask = listener.GetContextAsync();
            Task timeoutTask = Task.Delay(TimeSpan.FromMinutes(3), cancellationToken);
            Task completed = await Task.WhenAny(contextTask, timeoutTask);
            if (completed != contextTask)
            {
                throw new GoogleSignInException("Google sign-in timed out before the browser returned to the app.");
            }

            return await contextTask;
        }

        private static async Task WriteBrowserResponseAsync(HttpListenerResponse response, bool isSuccess, string title, string message)
        {
            string accent = isSuccess ? "#5CCB96" : "#FF8C7A";
            string html =
                "<!doctype html><html><head><meta charset=\"utf-8\"><title>simpleDocs sign-in</title></head>" +
                "<body style=\"margin:0;font-family:Segoe UI,Arial,sans-serif;background:#0f1724;color:#f7f8fb;display:flex;min-height:100vh;align-items:center;justify-content:center;\">" +
                $"<div style=\"max-width:460px;padding:32px 36px;border-radius:22px;background:#162132;box-shadow:0 20px 50px rgba(0,0,0,0.35);border:1px solid rgba(255,255,255,0.08);\">" +
                $"<div style=\"display:inline-block;padding:6px 12px;border-radius:999px;background:rgba(255,255,255,0.08);color:{accent};font-size:12px;font-weight:600;letter-spacing:0.02em;\">simpleDocs</div>" +
                $"<h1 style=\"margin:18px 0 10px;font-size:28px;font-weight:600;\">{WebUtility.HtmlEncode(title)}</h1>" +
                $"<p style=\"margin:0;color:#c6d2e3;font-size:15px;line-height:1.5;\">{WebUtility.HtmlEncode(message)}</p>" +
                "</div></body></html>";

            byte[] bytes = Encoding.UTF8.GetBytes(html);
            response.StatusCode = 200;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }

        private static string CreateCodeChallenge(string codeVerifier)
        {
            byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
            return Base64UrlEncode(hash);
        }

        private static string CreateRandomBase64Url(int byteCount)
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(byteCount);
            return Base64UrlEncode(bytes);
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string MapGoogleError(string error)
        {
            return error switch
            {
                "access_denied" => "Google sign-in was canceled.",
                _ => "Google sign-in did not complete successfully."
            };
        }
    }

    internal sealed class GoogleAuthorizationResult
    {
        public string AuthorizationCode { get; init; } = string.Empty;
        public string CodeVerifier { get; init; } = string.Empty;
        public string RedirectUri { get; init; } = string.Empty;
    }

    internal sealed class GoogleSignInException : Exception
    {
        public GoogleSignInException(string message)
            : base(message)
        {
        }

        public GoogleSignInException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
