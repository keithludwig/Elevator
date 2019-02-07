using System;
using System.Collections.Generic;
using System.Threading;

namespace Elevator
{
	// This class exists mainly so the overall simulation can be tested and its output verified.
	public class Rider
	{
		public string		m_name;
		public int			m_destFloor;
	}

	// A floor is a container for riders that knows how to load/unload them into/from an elevator
	// when ElevatorArrived is called.
	public class Floor
	{
		public int				m_floor;
		public List<Rider>		m_riders;

		public Floor(int floor)
		{
			m_floor = floor;
			m_riders = new List<Rider>();
		}

		public void AddRider(Rider rider)
		{
			lock (m_riders)
				m_riders.Add(rider);
		}

		// This method is called by ElevatorController when an elevator arrives at a floor.
		public void ElevatorArrived(Elevator elev, Elevator.Direction elevDir)
		{
			lock (m_riders)
			{
				// First unload riders whose destination is this floor.
				List<Rider> unloadedRiders = elev.UnloadRiders();

				// Then load riders who are going the same direction as elevator.
				for (int i = m_riders.Count - 1; i >= 0; i--)
				{
					Rider rider = m_riders[i];
					Elevator.Direction riderDir = (rider.m_destFloor > m_floor) ? Elevator.Direction.Up : Elevator.Direction.Down;
					if (riderDir == elevDir && rider.m_destFloor != m_floor)
					{
						elev.LoadRider(rider);
						m_riders.RemoveAt(i);
					}
				}

				// Add unloaded riders back to floor.
				for (int i = 0; i < unloadedRiders.Count; i++)
					AddRider(unloadedRiders[i]);
			}
		}
	}

	// ElevatorController is in charge of the bank of elevators, and keeps a queue of
	// rider requests.  To service a request, ElevatorController attempts to find the best elevator
	// for the task, as dictated by FindBestElevator.  This method identifies the nearest
	// idle elevator, if one is available.  It also finds the nearest elevator that is
	// going in the right direction, and is above/below the requesting floor, as necessary.  Then
	// it picks whichever of these two is closest to the requesting floor.  If no suitable elevators
	// are found, the request remains in the queue until it can be processed.
	//
	public class ElevatorController
	{
		class FloorRequest
		{
			public int					m_floor;
			public Elevator.Direction	m_dir;

			public FloorRequest(int floor, Elevator.Direction dir)		{ m_floor = floor; m_dir = dir; }
		}

		const int c_defaultRunIntervalMsecs		= 1000;		// Sleep interval when request can't be immediately serviced.

		internal Floor[]			Floors;			// List of floors
		internal Elevator[]			Elevators;		// The bank of elevators

		bool						m_running;
		Queue<FloorRequest>			m_requests;		// List of outstanding elevator requests from floors.

		public void Start(int numFloors, int numElevators)
		{
			Floors = new Floor[numFloors];
			for (int i = 0; i < numFloors; i++)
				Floors[i] = new Floor(i);

			Elevators = new Elevator[numElevators];
			for (int i = 0; i < numElevators; i++)
			{
				Elevators[i] = new Elevator(Convert.ToChar(i + 65), numFloors);		// Give each elevator a letter as ID
				Elevators[i].OnElevatorEvent += HandleElevatorEvent;
			}

			// Startup thread to handle floor requests
			m_requests = new Queue<FloorRequest>();
			Thread thread = new Thread(new ThreadStart(this.Run));
			thread.Start();

			// Startup elevator threads
			for (int i = 0; i < numElevators; i++)
			{
				thread = new Thread(new ThreadStart(Elevators[i].Run));
				thread.Start();
			}
		}

		public void Stop()
		{
			for (int i = 0; i < Elevators.Length; i++)
				Elevators[i].Stop();

			m_running = false;
		}

		public void RequestElevator(int floor, Elevator.Direction dir)
		{
			lock (m_requests)
				m_requests.Enqueue(new FloorRequest(floor, dir));
		}

		void Run()
		{
			// This is the main loop for the ElevatorController thread.  It attempts to service
			// requests from the queue in order.  If one can't be serviced, the thread sleeps
			// for 1 second, then tries again.

			m_running = true;
			while (m_running)
			{
				while (m_requests.Count > 0)
				{
					bool handled = false;
					lock (m_requests)
					{
						FloorRequest req = m_requests.Peek();		// Peek at next floor request
						int idx = FindBestElevator(req.m_floor, req.m_dir);
						if (idx >= 0)
						{
							m_requests.Dequeue();	// Dequeue the request and send to elevator
							Elevators[idx].RequestFloor(req.m_floor, req.m_dir);
							handled = true;
						}
					}
					
					// If none handled, sleep for a bit to allow elevators to continue their work.
					if (!handled)
						Thread.Sleep(c_defaultRunIntervalMsecs);
				}
			}
		}

		int FindBestElevator(int floor, Elevator.Direction dir)
		{
			// This method chooses which elevator to route a particular request to.  It first finds the closest
			// idle elevator.  Then it finds the closest one moving in the proper direction (and above/below as necessary).
			// It then selects whichever of these is closest.  If none are found, the method returns -1.

			bool goingUp = dir == Elevator.Direction.Up;
			int closestIdleIdx = -1, closestIdleDist = int.MaxValue;
			int closestMovingIdx = -1, closestMovingDist = int.MaxValue;

			for (int i = 0; i < Elevators.Length; i++)
			{
				Elevators[i].GetState(out Elevator.State elevState, out Elevator.Direction elevDir, out int elevFloor);
				int dist = Math.Abs(floor - elevFloor);

				if (elevState == Elevator.State.Idle)
				{
					if (dist < closestIdleDist)
					{
						closestIdleIdx = i;
						closestIdleDist = dist;
					}
				}
				else if ((elevDir == dir) && (goingUp ? (elevFloor <= floor) : (elevFloor >= floor)))
				{
					if (dist < closestMovingDist)
					{
						closestMovingIdx = i;
						closestMovingDist = dist;
					}
				}
			}

			int bestIdx;
			if (closestIdleIdx == -1 && closestMovingIdx == -1)		// If no closest idle nor moving, return none found
				bestIdx = -1; 
			else if (closestMovingIdx == -1)						// If no closest moving, use closest idle
				bestIdx = closestIdleIdx;
			else if (closestIdleIdx == -1)							// If no closest idle, use closest moving
				bestIdx = closestMovingIdx;
			else
				bestIdx = closestMovingDist < closestIdleDist ? closestMovingIdx : closestIdleIdx;	// If both, choose closer one
			
			return bestIdx;
		}

		void HandleElevatorEvent(Elevator elev, Elevator.State state, int floor, Elevator.Direction dir)
		{
			if (state == Elevator.State.VisitingFloor)
				Floors[floor].ElevatorArrived(elev, dir);
		}
	}
}