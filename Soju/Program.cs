using Soju;
using Soju.Blockchain.Keys;

string scenarioFilePath = "test_scenario.json";

int defaultAnonScoreTarget = KeyManager.DefaultAnonScoreTarget;
bool defaultRedCoinIsolation = KeyManager.DefaultRedCoinIsolation;

ScenarioParser parser = new(defaultAnonScoreTarget, defaultRedCoinIsolation);
ScenarioConfig? scenario = parser.Parse(scenarioFilePath);
foreach (string error in parser.Errors)
{
	Console.WriteLine($"[ERROR] {error}");
}
foreach (string warning in parser.Warnings)
{
	Console.WriteLine($"[WARNING] {warning}");
}
if (scenario is null || parser.Errors.Count > 0)
{
	return 1;
}

ScenarioRunner runner = new();
runner.Run(scenario);

return 0;