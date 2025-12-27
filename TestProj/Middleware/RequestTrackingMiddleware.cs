using Microsoft.AspNetCore.Http;
using System.Diagnostics;

namespace TestProj.Middleware
{
    public class RequestTrackingMiddleware
    {
        private readonly RequestDelegate _next;

        public RequestTrackingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var id = Guid.NewGuid().ToString();

            var requestInfo = new RequestInfo
            {
                Id = id,
                Method = context.Request.Method,
                Path = context.Request.Path.ToString(),
                ClientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                StartTime = DateTime.UtcNow,
                Status = "InProgress",
                State = "RequestReceived",
                TraceId = Activity.Current?.TraceId.ToString() ?? "none"
            };

            RequestTracker.Add(requestInfo);

            // 1️⃣ Detect client disconnect
            var abortToken = context.RequestAborted;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, abortToken);
                }
                catch (TaskCanceledException)
                {
                    requestInfo.Status = "ClientDisconnected";
                    requestInfo.State = "Disconnected";
                    requestInfo.EndTime = DateTime.UtcNow;
                    RequestTracker.Remove(id);
                }
            });

            try
            {
                // 2️⃣ Routing stage
                requestInfo.State = "Routing";
                await Task.Yield();

                // 3️⃣ Executing handler stage (default)
                requestInfo.State = "ExecutingHandler";

                await _next(context);

                // 4️⃣ Writing response
                requestInfo.State = "WritingResponse";
                requestInfo.Status = "Completed";
            }
            catch
            {
                requestInfo.Status = "Error";
                requestInfo.State = "Error";
                throw;
            }
            finally
            {
                // 5️⃣ Deep state injected by controller (Redis, DB, HTTP)
                var deepState = RequestStateHelper.GetState(context);
                if (!string.IsNullOrEmpty(deepState))
                    requestInfo.State = deepState;

                // Only remove if not removed by disconnect logic
                if (requestInfo.Status != "ClientDisconnected")
                {
                    requestInfo.EndTime = DateTime.UtcNow;
                    RequestTracker.Remove(id);
                }
            }
        }
    }

    public class RequestInfo
    {
        public string Id { get; set; } = default!;
        public string Method { get; set; } = default!;
        public string Path { get; set; } = default!;
        public string ClientIp { get; set; } = default!;
        public string TraceId { get; set; } = default!;
        public string State { get; set; } = default!;
        public string Status { get; set; } = default!;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        public double DurationMs =>
            ((EndTime ?? DateTime.UtcNow) - StartTime).TotalMilliseconds;
    }
}
