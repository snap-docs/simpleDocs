using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeExplainer
{
    internal sealed class BrowserAuthCoordinator
    {
        private readonly ClientConfig _config;

        public BrowserAuthCoordinator(ClientConfig config)
        {
            _config = config;
        }

        public string RedirectUri => BuildRedirectUri(_config.AuthBrowserCallbackPort);

        public async Task<BrowserAuthorizationResult> AuthorizeAsync(string authorizationUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(authorizationUrl))
            {
                throw new BrowserAuthException("The authentication service did not return a browser sign-in URL.");
            }

            string redirectUri = RedirectUri;
            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);

            try
            {
                listener.Start();
            }
            catch (Exception ex)
            {
                throw new BrowserAuthException("Unable to start the local sign-in callback listener.", ex);
            }

            try
            {
                try
                {
                    Process.Start(new ProcessStartInfo(authorizationUrl)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    throw new BrowserAuthException("Unable to open the browser for sign-in.", ex);
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
                        throw new BrowserAuthException(MapProviderError(error));
                    }

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        await WriteBrowserResponseAsync(context.Response, false, "Sign-in failed", "The provider did not return an authorization code.");
                        throw new BrowserAuthException("The sign-in flow did not return an authorization code.");
                    }

                    await WriteBrowserResponseAsync(context.Response, true, "Sign-in complete", "You can close this window and return to simpleDocs.");

                    return new BrowserAuthorizationResult
                    {
                        AuthorizationCode = code,
                        State = returnedState,
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

        private static async Task<HttpListenerContext> WaitForCallbackAsync(HttpListener listener, CancellationToken cancellationToken)
        {
            Task<HttpListenerContext> contextTask = listener.GetContextAsync();
            Task timeoutTask = Task.Delay(TimeSpan.FromMinutes(3), cancellationToken);
            Task completed = await Task.WhenAny(contextTask, timeoutTask);
            if (completed != contextTask)
            {
                throw new BrowserAuthException("The sign-in flow timed out before the browser returned to the app.");
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
                $"<div style=\"display:inline-block;padding:6px 12px;border-radius:999px;background:rgba(255,255,255,0.08);color:{accent};font-size:12px;font-weight:600;\">simpleDocs</div>" +
                $"<h1 style=\"margin:18px 0 10px;font-size:28px;font-weight:600;\">{WebUtility.HtmlEncode(title)}</h1>" +
                $"<p style=\"margin:0;color:#c6d2e3;font-size:15px;line-height:1.5;\">{WebUtility.HtmlEncode(message)}</p>" +
                "</div></body></html>";

            byte[] bytes = Encoding.UTF8.GetBytes(html);
            response.StatusCode = 200;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }

        private static string MapProviderError(string error)
        {
            return error switch
            {
                "access_denied" => "The sign-in flow was canceled.",
                _ => "The sign-in flow did not complete successfully."
            };
        }
    }

    internal sealed class BrowserAuthorizationResult
    {
        public string AuthorizationCode { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public string RedirectUri { get; init; } = string.Empty;
    }

    internal sealed class BrowserAuthException : Exception
    {
        public BrowserAuthException(string message)
            : base(message)
        {
        }

        public BrowserAuthException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
