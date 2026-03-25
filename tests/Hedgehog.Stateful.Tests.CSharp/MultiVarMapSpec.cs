using Hedgehog.Linq;
using Hedgehog.Stateful.Linq;
using Range = Hedgehog.Linq.Range;

namespace Hedgehog.Stateful.Tests.CSharp;

public sealed record Point(Guid Id, string Name);

public sealed record PointSymbolic(Var<Guid> Id, string Name);

public class Syllabus
{
    public IReadOnlyList<Point> AddPoints(IEnumerable<string> pointNames)
    {
        return pointNames.Select(name => new Point(Guid.NewGuid(), name)).ToList();
    }
}

public class AddPointsCommand : Command<Syllabus, List<PointSymbolic>, List<string>, IReadOnlyList<Point>>
{
    public override string Name => "AddPoints";

    public override bool Precondition(List<PointSymbolic> state) => true;

    public override bool Require(Env env, List<PointSymbolic> state, List<string> value) => Precondition(state);

    public override Task<IReadOnlyList<Point>> Execute(Syllabus sut, Env env, List<PointSymbolic> state,
        List<string> value)
    {
        var points = sut.AddPoints(value);
        return Task.FromResult(points);
    }

    public override Gen<List<string>> Generate(List<PointSymbolic> state) =>
        // Generate a list of random and unique point names to add
        GenExtensions.String(Gen.Alpha, Range.Constant(3, 6)).List(Range.Constant(5, 10))
            .Where(l => l.Distinct().Count() == l.Count());

    // state is just last list of points
    public override List<PointSymbolic> Update(List<PointSymbolic> state, List<string> input,
        Var<IReadOnlyList<Point>> outputVar) =>
        input.Select(name =>
            new PointSymbolic(outputVar.Select(o =>
                o.First(p =>
                    p.Name == name).Id), name)).ToList();
}

public class AddPointsCommand_VarsResolve : AddPointsCommand
{
    public override bool Ensure(Env env, List<PointSymbolic> oldState, List<PointSymbolic> newState, List<string> input,
        IReadOnlyList<Point> output)
    {
        var resolvedState = newState.Select(p => new Point(p.Id.Resolve(env), p.Name));
        return resolvedState.SequenceEqual(output);
    }
}

public class AddPointsCommand_VarsDistinct : AddPointsCommand
{
    public override bool Ensure(Env env, List<PointSymbolic> oldState, List<PointSymbolic> newState, List<string> input,
        IReadOnlyList<Point> output)
        => newState.Select(p => p.Id).Distinct().Count() == newState.Count;
}

public class MultiVarMapSpec : SequentialSpecification<Syllabus, List<PointSymbolic>>
{
    public override List<PointSymbolic> InitialState => [];
    public override Range<int> Range => Hedgehog.Linq.Range.Singleton(1);

    public override ICommand<Syllabus, List<PointSymbolic>>[] Commands =>
    [
        // new AddPointsCommand_VarsResolve()
        new AddPointsCommand_VarsDistinct()
    ];
}

public class MultiVarMapTests
{
    [Fact]
    public void MultiVarMapTest()
    {
        var sut = new Syllabus();
        new MultiVarMapSpec().ToProperty(sut).Check();
    }
}
