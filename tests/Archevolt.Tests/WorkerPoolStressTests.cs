namespace Archevolt.Tests;

[Trait("Category", "Scheduling")]
public sealed class WorkerPoolStressTests
{
	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(4)]
	public void Reused_public_schedules_execute_thousands_of_varied_batches_exactly_once(int budget)
	{
		using var world = new WorldBuilder().UseParallelExecution(budget).Build();
		int[] sizes = [0, 1, 2, 31, 257];
		var state = new ExecutionState();
		var schedules = new List<BoundSchedule>();
		try
		{
			foreach (int size in sizes)
			{
				var descriptors = new SystemDescriptor[size];
				for (int item = 0; item < size; item++)
				{
					int index = item;
					descriptors[item] = SystemDescriptor.Create("work" + item,
						_ => new WorkBody(index, state)).Build();
				}
				schedules.Add(BoundSchedule.Bind(world, descriptors));
			}
			for (int iteration = 0; iteration < 1_200; iteration++)
			{
				TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
				Array.Clear(state.Hits);
				state.Maximum = 0;
				state.Iteration = iteration;
				int pattern = iteration % sizes.Length;
				schedules[pattern].Run(TestContext.Current.CancellationToken);
				Assert.Equal(0, state.Active);
				Assert.InRange(state.Maximum, 0, budget);
				for (int item = 0; item < state.Hits.Length; item++)
					Assert.Equal(item < sizes[pattern] ? 1 : 0, state.Hits[item]);
			}
		}
		finally
		{
			foreach (var schedule in schedules) schedule.Dispose();
		}
	}

	private sealed class ExecutionState
	{
		internal readonly int[] Hits = new int[257];
		internal int Active;
		internal int Maximum;
		internal int Iteration;
	}

	private sealed class WorkBody(int index, ExecutionState state) : SystemBody
	{
		protected override void Run(SystemRun run)
		{
			int active = Interlocked.Increment(ref state.Active);
			int observed = Volatile.Read(ref state.Maximum);
			while (active > observed)
			{
				int previous = Interlocked.CompareExchange(ref state.Maximum, active, observed);
				if (previous == observed) break;
				observed = previous;
			}
			try
			{
				if (((state.Iteration + index) & 7) == 0) Thread.Yield();
				Interlocked.Increment(ref state.Hits[index]);
			}
			finally
			{
				Interlocked.Decrement(ref state.Active);
			}
		}
	}
}
