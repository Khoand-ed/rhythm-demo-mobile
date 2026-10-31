using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Leaderboards;
using Promuse.Net;
using UnityEngine;
using UnityEngine.TestTools;

// 一次只刷新一次 / Several requests that find the session's access token expired at the same moment -
// Home asks for half a dozen things at once - must refresh it ONCE between them. The server rotates
// the refresh token on every use and treats a second use of the same one as theft: it revokes the
// whole session. Each request refreshing on its own signed the player out about fifteen minutes in,
// the access token's lifetime.
//
// Against a small server of the test's own (FakeAuthServer, below) that rotates and revokes the way
// the real one does, so the client is exercised end to end - UnityWebRequest included - without a
// database or the dev API.
public class TokenRefreshTests
{
    private FakeAuthServer server;
    private string scope;
    private TokenStore tokens;
    private PromuseApi api;

    [SetUp]
    public void SetUp()
    {
        server = new FakeAuthServer();
        // A scope of the test's own: neither the session nor the files of a real server are touched.
        scope = "test-" + Guid.NewGuid().ToString("N");
        tokens = new TokenStore(scope);
        var config = new PromuseConfig
        {
            baseUrl = server.Url,
            storageScope = scope,
            timeoutSeconds = 10,
            maxRetries = 0,
        };
        api = new PromuseApi(config, tokens);
    }

    [TearDown]
    public void TearDown()
    {
        tokens.Clear();
        server.Dispose();
        try { Directory.Delete(api.DataDirectory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static TokenPair Pair(string access, int expiresIn, string refresh) =>
        new TokenPair(access, TokenPair.Bearer, expiresIn, refresh, 2592000);

    private IEnumerator Await(Task task)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.IsTrue(task.IsCompleted, "the requests did not finish");
    }

    private List<Task<ApiResult<LeaderboardPage>>> FiveAtOnce()
    {
        var calls = new List<Task<ApiResult<LeaderboardPage>>>();
        for (int i = 0; i < 5; i++) calls.Add(api.GetLeaderboardAsync("stage_" + i));
        return calls;
    }

    [UnityTest]
    public IEnumerator AnAccessTokenExpiredHere_IsRefreshedOnce_ForEveryRequestWaitingOnIt()
    {
        // Expired by the device's own clock: every request refreshes before it is sent.
        tokens.Adopt(Pair("a0", 0, "r0"));

        List<Task<ApiResult<LeaderboardPage>>> calls = FiveAtOnce();
        yield return Await(Task.WhenAll(calls));

        Assert.AreEqual(1, server.Refreshes, "one refresh, shared by all five");
        Assert.IsFalse(server.Revoked, "the server never saw the same refresh token twice");
        foreach (Task<ApiResult<LeaderboardPage>> call in calls) Assert.IsTrue(call.Result.IsSuccess, call.Result.Message);
        Assert.IsTrue(tokens.HasSession, "still signed in");
        Assert.AreEqual("r1", tokens.RefreshToken);
    }

    [UnityTest]
    public IEnumerator AnAccessTokenTheServerRefuses_IsRefreshedOnce_AndEveryRequestIsSentAgain()
    {
        // Valid by the device's clock but refused by the server (its clock ran ahead, say): five 401s.
        tokens.Adopt(Pair("stale", 900, "r0"));

        List<Task<ApiResult<LeaderboardPage>>> calls = FiveAtOnce();
        yield return Await(Task.WhenAll(calls));

        Assert.AreEqual(1, server.Refreshes, "one refresh for five 401s");
        Assert.IsFalse(server.Revoked);
        foreach (Task<ApiResult<LeaderboardPage>> call in calls) Assert.IsTrue(call.Result.IsSuccess, call.Result.Message);
        Assert.IsTrue(tokens.HasSession);
    }

    [UnityTest]
    public IEnumerator SigningOutWhileARefreshIsInFlight_StaysSignedOut()
    {
        tokens.Adopt(Pair("a0", 0, "r0"));
        server.RefreshDelayMs = 600;

        Task<ApiResult<LeaderboardPage>> call = api.GetLeaderboardAsync("stage");
        Task signOut = api.SignOutAsync();
        yield return Await(Task.WhenAll(call, signOut));

        Assert.IsFalse(tokens.HasSession, "the refresh that answered after the sign-out did not bring the session back");
    }

    [UnityTest]
    public IEnumerator SigningOut_LetsGoOfTheSession_BeforeTheServerHasAnswered()
    {
        tokens.Adopt(Pair("a0", 900, "r0"));

        Task signOut = api.SignOutAsync();

        // The login screen a sign-out returns to reopens any session it finds; none may be left for it
        // while the logout is still on the wire.
        Assert.IsFalse(signOut.IsCompleted, "the server has not answered yet");
        Assert.IsFalse(tokens.HasSession);
        Assert.IsFalse(new TokenStore(scope).HasSession, "nor on the device");

        yield return Await(signOut);

        Assert.AreEqual(1, server.Logouts);
        Assert.AreEqual("r0", server.LoggedOut, "the server is still told which session ended");
    }

    // ------------------------------------------------------------------------- the server

    /// <summary>
    /// Just enough of the API on a loopback socket: /v1/auth/refresh rotates the pair and answers a
    /// refresh token presented twice with REFRESH_TOKEN_REUSED, revoking everything, as
    /// AuthService does; /v1/leaderboards/* wants the current access token; /v1/auth/logout says yes,
    /// and remembers the token it was given. HTTP/1.1, one request per connection.
    /// </summary>
    private sealed class FakeAuthServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly Thread acceptor;
        private readonly object gate = new object();
        private readonly HashSet<string> spent = new HashSet<string>();
        private string liveRefresh = "r0";
        private string liveAccess = "a0";
        private int refreshes;
        private bool revoked;
        private int logouts;
        private string loggedOut;
        private volatile bool stopping;

        public int RefreshDelayMs = 200;

        public FakeAuthServer()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            acceptor = new Thread(Accept) { IsBackground = true, Name = "FakeAuthServer" };
            acceptor.Start();
        }

        public string Url { get; }
        public int Refreshes { get { lock (gate) return refreshes; } }
        public bool Revoked { get { lock (gate) return revoked; } }
        public int Logouts { get { lock (gate) return logouts; } }

        /// <summary>The refresh token the last logout presented.</summary>
        public string LoggedOut { get { lock (gate) return loggedOut; } }

        private void Accept()
        {
            while (!stopping)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (SocketException) { return; }
                catch (ObjectDisposedException) { return; }
                ThreadPool.QueueUserWorkItem(_ => Serve(client));
            }
        }

        private void Serve(TcpClient client)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                try
                {
                    string head = ReadHead(stream, out byte[] rest);
                    string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    string[] requestLine = lines[0].Split(' ');
                    string path = requestLine[1];
                    int length = 0;
                    string authorization = null;
                    foreach (string line in lines)
                    {
                        int colon = line.IndexOf(':');
                        if (colon <= 0) continue;
                        string name = line.Substring(0, colon).Trim();
                        string value = line.Substring(colon + 1).Trim();
                        if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) length = int.Parse(value);
                        if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) authorization = value;
                    }
                    string body = ReadBody(stream, rest, length);

                    int status;
                    string reply = Route(path, authorization, body, out status);
                    Write(stream, status, reply);
                }
                catch (IOException) { }
            }
        }

        private string Route(string path, string authorization, string body, out int status)
        {
            if (path == "/v1/auth/refresh")
            {
                string presented = Regex.Match(body, "\"refreshToken\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
                string access, refresh;
                lock (gate)
                {
                    if (revoked || spent.Contains(presented))
                    {
                        revoked = true;
                        status = 401;
                        return Problem(401, "REFRESH_TOKEN_REUSED");
                    }
                    if (presented != liveRefresh)
                    {
                        status = 401;
                        return Problem(401, "REFRESH_TOKEN_INVALID");
                    }
                    // Spent the moment it arrives, as the real server's conditional UPDATE does: a second
                    // refresh with it, even one already on the wire, is a reuse.
                    spent.Add(presented);
                    refreshes++;
                    liveRefresh = "r" + refreshes;
                    liveAccess = "a" + refreshes;
                    access = liveAccess;
                    refresh = liveRefresh;
                }
                Thread.Sleep(RefreshDelayMs);
                status = 200;
                return "{\"accessToken\":\"" + access + "\",\"tokenType\":\"Bearer\",\"expiresIn\":900," +
                       "\"refreshToken\":\"" + refresh + "\",\"refreshExpiresIn\":2592000}";
            }

            if (path == "/v1/auth/logout")
            {
                lock (gate)
                {
                    logouts++;
                    loggedOut = Regex.Match(body, "\"refreshToken\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
                }
                status = 204;
                return "";
            }

            if (path.StartsWith("/v1/leaderboards/", StringComparison.Ordinal))
            {
                lock (gate)
                {
                    if (revoked || authorization != "Bearer " + liveAccess)
                    {
                        status = 401;
                        return Problem(401, "UNAUTHORIZED");
                    }
                }
                status = 200;
                return "{\"stageId\":\"stage\",\"periodKey\":\"all\",\"entries\":[],\"serverTime\":\"2026-10-24T00:00:00Z\"}";
            }

            status = 404;
            return Problem(404, "NOT_FOUND");
        }

        private static string Problem(int status, string code) =>
            "{\"type\":\"about:blank\",\"title\":\"" + code + "\",\"status\":" + status + ",\"code\":\"" + code + "\"}";

        private static string ReadHead(NetworkStream stream, out byte[] rest)
        {
            var buffer = new List<byte>();
            var chunk = new byte[1024];
            while (true)
            {
                int read = stream.Read(chunk, 0, chunk.Length);
                if (read <= 0) throw new IOException("closed before the headers ended");
                for (int i = 0; i < read; i++) buffer.Add(chunk[i]);
                int end = IndexOfHeaderEnd(buffer);
                if (end >= 0)
                {
                    rest = buffer.GetRange(end + 4, buffer.Count - end - 4).ToArray();
                    return Encoding.ASCII.GetString(buffer.GetRange(0, end).ToArray());
                }
            }
        }

        private static int IndexOfHeaderEnd(List<byte> b)
        {
            for (int i = 0; i + 3 < b.Count; i++)
                if (b[i] == '\r' && b[i + 1] == '\n' && b[i + 2] == '\r' && b[i + 3] == '\n') return i;
            return -1;
        }

        private static string ReadBody(NetworkStream stream, byte[] already, int length)
        {
            var body = new List<byte>(already);
            var chunk = new byte[1024];
            while (body.Count < length)
            {
                int read = stream.Read(chunk, 0, Math.Min(chunk.Length, length - body.Count));
                if (read <= 0) break;
                for (int i = 0; i < read; i++) body.Add(chunk[i]);
            }
            return Encoding.UTF8.GetString(body.ToArray());
        }

        private static void Write(NetworkStream stream, int status, string body)
        {
            byte[] payload = Encoding.UTF8.GetBytes(body);
            string reason = status == 200 ? "OK" : status == 204 ? "No Content" : status == 401 ? "Unauthorized" : "Not Found";
            string head = "HTTP/1.1 " + status + " " + reason + "\r\n" +
                          "Content-Type: application/json\r\n" +
                          "Content-Length: " + payload.Length + "\r\n" +
                          "Connection: close\r\n\r\n";
            byte[] headBytes = Encoding.ASCII.GetBytes(head);
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        public void Dispose()
        {
            stopping = true;
            listener.Stop();
        }
    }
}
