using WlanLivePathTester.Core.Operations;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private Task RunMeasurementOperationAsync(
        Func<CancellationToken, Task> operation,
        string runningMessage) =>
        RunMeasurementOperationAsync(
            ApplicationOperationKind.RepeatedMeasurement,
            operation,
            runningMessage);
}
