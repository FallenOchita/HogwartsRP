# Architecture

HogwartsRP is a .NET 10 server host with dependency-inverted networking and game integration contracts. There is no multiplayer transport or Hogwarts Legacy adapter in this milestone. The local host and examples are framework demonstrations only.

The server discovers resource folders, parses the restricted manifest grammar without executing manifests, sorts dependencies deterministically, and creates a separate scripting state per server/shared Lua file. Resource script paths are constrained to their resource folder. Core framework assemblies are never reloaded.

## Runtime status

MoonSharp is MIT licensed but implements Lua 5.2, not the required Lua 5.4. It is used as a replaceable interim adapter to run basic sample scripts; it does not satisfy Lua 5.4 compatibility. Its standard library selection omits operating system, IO, package, debug, and coroutine modules. This is an in-process sandbox, not a security boundary against hostile code, and this milestone does not set hard CPU/memory quotas. A production host must use native Lua 5.4 in a constrained process or equivalent isolation.

## Security and actual game integration

Permission checks default to deny. Network events are represented by a validated adapter contract; no adapter is installed. Client calls cannot grant permissions. Game character/house and character persistence interfaces are contracts only, and return unavailable until a real adapter is supplied. No game files, authentication, anti-cheat, or DRM are accessed or bypassed.

The launcher is separated behind installation detection, platform launch, and character creation bridge interfaces. Its Windows detector checks conventional install roots plus caller-provided roots. The Steam adapter asks Steam to run app 990080 so ownership and authentication stay with the platform. Epic launch is not wired because no documented platform launch contract is included. Official mod support documents content creation and in-game mod management, but no direct character-creation launch API was found. The bridge therefore falls back to normal startup. Character submissions are validated on the server, with ownership derived from the authenticated session.
