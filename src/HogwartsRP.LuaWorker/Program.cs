using HogwartsRP.Scripting;

if (args.Length != 1 || args[0] != "--worker") return 2;
LinuxWorkerLimits.ApplyAtWorkerStartup();
return await LuaWorkerHost.RunAsync();

public sealed class WorkerAssemblyMarker { }
