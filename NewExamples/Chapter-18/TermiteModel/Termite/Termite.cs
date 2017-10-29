using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;
using Microsoft.ServiceFabric.Actors.Client;
using Termite.Interfaces;
using Box.Interfaces;
using Microsoft.ServiceFabric.Services.Remoting.Client;

namespace Termite
{
    /// <remarks>
    /// This class represents an actor.
    /// Every ActorID maps to an instance of this class.
    /// The StatePersistence attribute determines persistence and replication of actor state:
    ///  - Persisted: State is written to disk and replicated.
    ///  - Volatile: State is kept in memory only and replicated.
    ///  - None: State is kept in memory only and not replicated.
    /// </remarks>
    [StatePersistence(StatePersistence.Persisted)]
    internal class Termite : Actor, ITermite
    {
        #region Private vars
        private IActorTimer mTimer;
        private static Random rand = new Random();
        private string actorStateName = "TermiteActorState";
        private const int size = 100;
        #endregion

        /// <summary>
        /// Initializes a new instance of Termite
        /// </summary>
        /// <param name="actorService">The Microsoft.ServiceFabric.Actors.Runtime.ActorService that will host this actor instance.</param>
        /// <param name="actorId">The Microsoft.ServiceFabric.Actors.ActorId for this actor instance.</param>
        public Termite(ActorService actorService, ActorId actorId)
            : base(actorService, actorId)
        {
        }

        public Task<TermiteState> GetStateAsync()
        {
            var state = this.StateManager.TryGetStateAsync<TermiteState>(actorStateName);
            if (state.Result.HasValue)
                return Task.FromResult(state.Result.Value);
            return null;
        }

        /// <summary>
        /// This method is called whenever an actor is activated.
        /// An actor is activated the first time any of its methods are invoked.
        /// </summary>
        protected override Task OnActivateAsync()
        {
            ActorEventSource.Current.ActorMessage(this, "Actor activated.");

            var state = this.StateManager.TryGetStateAsync<TermiteState>(actorStateName);
            if (state.Result.HasValue == false)
            {
                this.StateManager.SetStateAsync(actorStateName, new TermiteState()
                {
                    X = rand.Next(0, size),
                    Y = rand.Next(0, size),
                    HasWoodChip = false
                });
                state = this.StateManager.TryGetStateAsync<TermiteState>(actorStateName);
                mTimer = RegisterTimer(Move, state, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50));
            }

            // The StateManager is this actor's private state store.
            // Data stored in the StateManager will be replicated for high-availability for actors that use volatile or persisted state storage.
            // Any serializable object can be saved in the StateManager.
            // For more information, see https://aka.ms/servicefabricactorsstateserialization

            return Task.FromResult(true);
        }

        protected override Task OnDeactivateAsync()
        {
            if (mTimer != null)
                UnregisterTimer(mTimer);

            return base.OnDeactivateAsync();
        }

        private async Task Move(object val)
        {
            IBox boxClient = ServiceProxy.Create<IBox>(new Uri("fabric:/TermiteModel/Box"));
            var state = await this.GetStateAsync();

            if (!state.HasWoodChip)
            {
                var result = await boxClient.TryPickUpWoodChipAsync(state.X, state.Y);
                if (result)
                {
                    state.HasWoodChip = true;
                }
            }
            else
            {
                var result = await boxClient.TryPutDownWoodChipAsync(state.X, state.Y);
                if (result)
                {
                    state.HasWoodChip = false;
                }
            }

            int action = rand.Next(1, 9);
            //1-left; 2-left-up; 3-up; 4-up-right; 5-right: 6-right-down; 7-down; 8-down-left
            if ((action == 1 || action == 2 || action == 8) && state.X > 0)
                state.X = state.X - 1;
            if ((action == 4 || action == 5 || action == 6) && state.X < size - 1)
                state.X = state.X + 1;
            if ((action == 2 || action == 3 || action == 4) && state.Y > 0)
                state.Y = state.Y - 1;
            if ((action == 6 || action == 7 || action == 8) && state.Y < size - 1)
                state.Y = state.Y + 1;

            await this.SaveStateAsync();
        }
    }
}
