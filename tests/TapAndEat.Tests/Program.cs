using TapAndEat.Tests.Framework;
using TapAndEat.Tests.Suites;

var runner = new TestRunner();

AuthServiceTests.Register(runner);
MenuServiceTests.Register(runner);
AdminServiceTests.Register(runner);

Console.WriteLine("Tap & Eat — Sprint 1 test suite");
Console.WriteLine("(hand-rolled runner — see TapAndEat.Tests.csproj for why there's no xUnit here)");

var exitCode = await runner.RunAllAsync();
return exitCode;
