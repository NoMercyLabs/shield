using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Shield.Api.Hubs;

// Server-push only. No client-callable methods — clients connect, subscribe to
// `findings.new` + `findings.counts`, and we broadcast from MatcherWorker /
// OpenCountsWatcher via IFindingsBroadcaster. Auth piggybacks on the default
// auth policy (cookie / JWT / SingleUser).
[Authorize]
public sealed class FindingsHub : Hub { }
