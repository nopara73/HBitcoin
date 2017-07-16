using NBitcoin;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NTumbleBit.ClassicTumbler.Client
{
	public class StateMachinesExecutor : TumblerServiceBase
	{
		public StateMachinesExecutor(
			TumblerClientRuntime runtime)
		{
			Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		}


		public TumblerClientRuntime Runtime
		{
			get; set;
		}

		public override string Name => "mixer";

		protected override void StartCore(CancellationToken cancellationToken)
		{
			Task.Run(async() =>
			{
				Debug.WriteLine("State machines started");
				var lastBlock = uint256.Zero;
				var lastCycle = 0;
				while (true)
				{
					Exception unhandled = null;
					try
					{
						lastBlock = await Runtime.Services.BlockExplorerService.WaitBlockAsync(lastBlock, cancellationToken).ConfigureAwait(false);
						var height = Runtime.Services.BlockExplorerService.GetCurrentHeight();
						Debug.WriteLine("New Block: " + height);
						var cycle = Runtime.TumblerParameters.CycleGenerator.GetRegistratingCycle(height);
						if(lastCycle != cycle.Start)
						{
							lastCycle = cycle.Start;
							Debug.WriteLine("New Cycle: " + cycle.Start);

							var state = Runtime.Repository.Get<PaymentStateMachine.State>(GetPartitionKey(cycle.Start), "");
							if(state == null)
							{
								var stateMachine = new PaymentStateMachine(Runtime, null);
								Save(stateMachine, cycle.Start);
							}
						}

						var cycles = Runtime.TumblerParameters.CycleGenerator.GetCycles(height);
						foreach(var state in cycles.SelectMany(c => Runtime.Repository.List<PaymentStateMachine.State>(GetPartitionKey(c.Start))))
						{
							var machine = new PaymentStateMachine(Runtime, state);
							try
							{
								await machine.UpdateAsync(default(CancellationToken)).ConfigureAwait(false);
								machine.InvalidPhaseCount = 0;
							}
							catch(PrematureRequestException)
							{
								Debug.WriteLine("Skipping update, need to wait for tor circuit renewal");
								break;
							}
							catch(Exception ex)
							{
								var invalidPhase = ex.Message.IndexOf("invalid-phase", StringComparison.OrdinalIgnoreCase) >= 0;

								if(invalidPhase)
									machine.InvalidPhaseCount++;
								else
									machine.InvalidPhaseCount = 0;

								if(!invalidPhase || machine.InvalidPhaseCount > 2)
								{
									Debug.WriteLine("ERROR: StateMachine Error: " + ex.ToString());
								}

							}
							Save(machine, machine.StartCycle);
						}
					}
					catch(OperationCanceledException ex)
					{
						if(cancellationToken.IsCancellationRequested)
						{
							Stopped();
							break;
						}
						else
							unhandled = ex;
					}
					catch(Exception ex)
					{
						unhandled = ex;
					}
					if(unhandled != null)
					{
						Debug.WriteLine("ERROR: StateMachineExecutor Error: " + unhandled.ToString());
						await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
					}
				}
			});
		}

		private static string GetPartitionKey(int cycle) => "Cycle_" + cycle;

		private void Save(PaymentStateMachine stateMachine, int cycle)
		{
			Runtime.Repository.UpdateOrInsert(GetPartitionKey(cycle), "", stateMachine.GetInternalState(), (o, n) => n);
		}
	}
}
