using Microsoft.AspNetCore.Http;

namespace TestProj.Middleware
{
    public static class RequestStateHelper
    {
        public static void SetState(HttpContext context, string state)
        {
            context.Items["RequestState"] = state;
        }

        public static string? GetState(HttpContext context)
        {
            return context.Items.TryGetValue("RequestState", out var value)
                ? value?.ToString()
                : null;
        }
    }
}
