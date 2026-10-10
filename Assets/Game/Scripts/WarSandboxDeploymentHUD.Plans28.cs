namespace MassEngine.Game {
 public sealed partial class WarSandboxDeploymentHUD {
  private bool planActionFailed28;
  public bool HasUncommittedInput28=>deployment!=null&&deployment.IsEditing&&HasPendingInputs();
 }
}
