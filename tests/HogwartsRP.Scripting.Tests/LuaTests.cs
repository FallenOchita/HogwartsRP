using HogwartsRP.Scripting;
namespace HogwartsRP.Scripting.Tests;

public class LuaTests
{
    [Fact]
    public void ExecutesLuaWithoutOperatingSystemLibraries()
    {
        using var lua = new MoonSharpRuntime();
        lua.Execute("result = 6 * 7", "test.lua");
        Assert.Throws<MoonSharp.Interpreter.ScriptRuntimeException>(() => lua.Execute("os.execute('whoami')", "blocked.lua"));
    }
}
