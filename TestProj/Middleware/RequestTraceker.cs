using System.Collections.Concurrent;

namespace TestProj.Middleware
{
    public static class RequestTracker
    {
        private static readonly ConcurrentDictionary<string, RequestInfo> _activeRequests =
            new ConcurrentDictionary<string, RequestInfo>();

        public static void Add(RequestInfo info)
        {
            _activeRequests[info.Id] = info;
        }

        public static void Remove(string id)
        {
            _activeRequests.TryRemove(id, out _);
        }

        public static IEnumerable<RequestInfo> GetActiveRequests()
        {
            return _activeRequests.Values.OrderBy(r => r.StartTime);
        }
    }
}
