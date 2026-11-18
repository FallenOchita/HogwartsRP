# Commands and events

The console accepts `status`, `quit`, and commands registered from Lua. The event bus dispatches local server events and returns disposable subscriptions. Network event validation and delivery belong to `INetworkingAdapter`; no transport is shipped. Never treat a client event as trusted authorization.
