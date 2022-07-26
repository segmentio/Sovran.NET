namespace Segment.Sovran
{
    public interface IAction
    {
        IState Reduce(IState state);
    }
}
