# Architecture

HogwartsRP is a .NET 10 server host with dependency-inverted networking and game integration contracts. There is no multiplayer transport or Hogwarts Legacy adapter in this milestone. The local host and examples are framework demonstrations only.

The server discovers resource folders, parses the restricted manifest grammar without executing manifests, sorts dependencies deterministically, and starts one Lua worker process and state per resource. Server/shared scripts within a resource share that resource's state. Resource script paths are constrained to their resource folder. Core framework assemblies are never reloaded.

## Runtime status

The scripting host uses NLua 1.7.9 with KeraLua's Lua 5.4 native runtime (MIT licensed). Each resource runs in its own worker process. The host removes OS, IO, package, debug, coroutine, CLR bridge, dynamic loader, and protected-call globals before resource code runs, then installs an instruction-count hook that fails over-budget scripts. The parent also enforces request timeouts and monitors worker private memory, terminating a worker that exceeds its configured limit. A worker still runs as the same OS user and is not protected by a restricted token, container, or OS sandbox; the memory monitor is supervisory rather than a kernel-enforced cap. Treat resource code as trusted until OS-level worker restrictions are added.

Resource-local events stay inside the worker. Command registration is relayed to the server and invocations route back to the owning worker. Cross-resource event routing is not implemented yet.

## Security and actual game integration

Permission checks default to deny. Network events are represented by a validated adapter contract; no adapter is installed. Client calls cannot grant permissions. Game character/house and character persistence interfaces are contracts only, and return unavailable until a real adapter is supplied. No game files, authentication, anti-cheat, or DRM are accessed or bypassed.

The launcher is separated behind installation detection, platform launch, and character creation bridge interfaces. Its Windows detector checks conventional install roots plus caller-provided roots. The Steam adapter asks Steam to run app 990080 so ownership and authentication stay with the platform. Epic launch is not wired because no documented platform launch contract is included. Official mod support documents content creation and in-game mod management, but no direct character-creation launch API was found. The bridge therefore falls back to normal startup. Character submissions are validated on the server, with ownership derived from the authenticated session.
