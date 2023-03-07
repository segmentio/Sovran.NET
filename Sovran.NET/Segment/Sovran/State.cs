namespace Segment.Sovran
{
    /// <summary>Generic state protocol.  All state structures must conform to this. It is highly
    /// recommended that *only* structs conform to this protocol. The system relies
    /// on a struct's built-in copy mechanism to function. Behavior when applied to classes
    /// is currently undefined and will likely result in errors.</summary>
    public interface IState
    {

    }
}
