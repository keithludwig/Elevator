using System;
using System.Collections.Generic;
using System.Threading;

namespace Elevator
{
	// This class contains the logic for a single elevator.  It has 3 states -- idle, moving, and visiting floor --
	// and has a current direction -- up, down, or none.  It starts off being idle and waits for a signal from the
	// elevator controller.  Then it chooses a starting direction based on the closest marked floor.  Once it moves in
	// a given direction, it will handle all requests in that direction before switching and handling requests
	// in the other direction.
	//
	// It also has logic for forcing the door to close (when open), or to remain open (when closing).  There is no
	// easy way to test this with the current command file capabilities, but the logic should be sound (and includes
	// a timer that expires after 60 seconds when forcing the door open).
	//
	public class Elevator
	{
		public enum State
		{
			Idle,
			Moving,
			VisitingFloor
		}

		public enum Direction
		{
			None,
			Up,
			Down
		}

		public delegate void ElevatorEventHandler(Elevator elev, State state, int floor, Direction dir);
		public event ElevatorEventHandler	OnElevatorEvent;

		const int			c_moveTimePerFloorSecs		= 3;		// seconds
		const int			c_doorRemainsOpenSecs		= 3;		// seconds
		const int			c_doorRemainsOpenMaxSecs	= 60;		// seconds
		const int			c_doorPollIntervalMsecs		= 100;		// milliseconds

		char				m_id;
		int					m_numFloors;
		State				m_state;
		Direction			m_direction;
		int					m_currFloor;
		bool[]				m_floorsUp;			// Which floors to visit going up
		bool[]				m_floorsDown;		// Which floors to visit going down
		bool				m_openDoorForce;	// The door is forced to remain open when this flag is true
		bool				m_closeDoorForce;	// The door closes immediately if this flag is true
		List<Rider>			m_riders;

		bool				m_running;
		AutoResetEvent		m_signal;
		object				m_lockObj;

		public Elevator(char id, int numFloors)
		{
			m_id				= id;
			m_numFloors			= numFloors;
			m_state				= State.Idle;
			m_direction			= Direction.None;
			m_currFloor			= 1;						// Ground floor
			m_floorsUp			= new bool[numFloors];
			m_floorsDown		= new bool[numFloors];
			m_openDoorForce		= false;
			m_closeDoorForce	= false;
			m_riders			= new List<Rider>();

			m_signal = new AutoResetEvent(false);
			m_lockObj = new object();

			for (int i = 0; i < m_numFloors; i++)
			{
				m_floorsUp[i] = false;
				m_floorsDown[i] = false;
			}
		}

		public void GetState(out State state, out Direction dir, out int currFloor)
		{
			lock (m_lockObj)
			{
				state		= m_state;
				dir			= m_direction;
				currFloor	= m_currFloor;
			}
		}

		// Main thread loop
		public void Run()
		{
			m_running = true;

			while (m_signal.WaitOne() && m_running)		// Wait until we are signaled.
			{
				lock (m_lockObj)
				{
					m_state = State.Moving;
					m_direction = Direction.None;
				}

				if (FindClosestMarkedFloor(out int closestMarkedFloor))		// Find closest marked floor.
				{
					lock (m_lockObj)
					{
						// Pick initial direction, if none is currently set.
						if (m_direction == Direction.None)
							m_direction = (closestMarkedFloor >= m_currFloor) ? Direction.Up : Direction.Down;
					}

					do
					{
						DoFloor();				// Process floor
					}
					while (MoveElevator());		// Move, optionally reversing direction if necessary
				}

				lock (m_lockObj)
				{
					m_state = State.Idle;
					m_direction = Direction.None;
					Console.WriteLine("{0:mm:ss} - {1}.{2}: idle", DateTime.Now, m_id, m_currFloor);
				}
			}
		}

		public void Stop()
		{
			m_running = false;
			m_signal.Set();
		}

		public void RequestFloor(int floor, Direction dir)
		{
			lock (m_lockObj)
			{
				if (dir == Direction.Up)
					m_floorsUp[floor] = true;
				else if (dir == Direction.Down)
					m_floorsDown[floor] = true;

				Console.WriteLine("{0:mm:ss} - {1}.{2}: floor {3} requested {4}.", DateTime.Now, m_id, m_currFloor, floor, dir);
			}

			m_signal.Set();
		}

		public void LoadRider(Rider rider)
		{
			// Mark the floor that the rider wants.
			lock (m_lockObj)
			{
				m_riders.Add(rider);
				if (rider.m_destFloor >= m_currFloor)
					m_floorsUp[rider.m_destFloor] = true;
				else
					m_floorsDown[rider.m_destFloor] = true;
				Console.WriteLine("{0:mm:ss} - {1}.{2}: {3} loaded, chose floor {4}", DateTime.Now, m_id, m_currFloor, rider.m_name, rider.m_destFloor);
			}

			m_signal.Set();
		}

		public List<Rider> UnloadRiders()
		{
			List<Rider> unloadedRiders = new List<Rider>();

			lock (m_lockObj)
			{
				for (int i = m_riders.Count - 1; i >= 0; i--)
				{
					if (m_riders[i].m_destFloor == m_currFloor)
					{
						Console.WriteLine("{0:mm:ss} - {1}.{2}: {3} unloaded", DateTime.Now, m_id, m_currFloor, m_riders[i].m_name);
						unloadedRiders.Add(m_riders[i]);
						m_riders.RemoveAt(i);
					}
				}
			}

			return unloadedRiders;
		}

		public void ForceOpenDoor(bool pressed)
		{
			m_openDoorForce = pressed;
		}

		public void ForceCloseDoor(bool pressed)
		{
			m_closeDoorForce = pressed;
		}
		
		///////////////////////////////////////////////////////
		// Private methods
		//

		void DoFloor()
		{
			Console.WriteLine("{0:mm:ss} - {1}.{2}: going {3}", DateTime.Now, m_id, m_currFloor, m_direction);

			bool floorMarked = false;
			lock (m_lockObj)			// See if this floor is marked, then clear the flag
			{
				floorMarked = (m_direction == Direction.Up) ? m_floorsUp[m_currFloor] : m_floorsDown[m_currFloor];

				if (m_direction == Direction.Up)
					m_floorsUp[m_currFloor] = false;
				else
					m_floorsDown[m_currFloor] = false;
			}

			if (floorMarked)			// If floor is marked, visit it
			{
				lock (m_lockObj)
					m_state = State.VisitingFloor;

				OpenDoor();
				OnElevatorEvent(this, m_state, m_currFloor, m_direction);	// This will cause riders to load/unload
				CloseDoor();

				lock (m_lockObj)
					m_state = State.Moving;
			}
		}

		void OpenDoor()
		{
			Console.WriteLine("{0:mm:ss} - {1}.{2}: open door", DateTime.Now, m_id, m_currFloor);

			// Leave door open for c_doorRemainsOpenSecs unless the force-close button is pressed.
			DateTime started = DateTime.Now;
			while (!m_closeDoorForce && DateTime.Now.Subtract(started).TotalSeconds < c_doorRemainsOpenSecs)
				Thread.Sleep(c_doorPollIntervalMsecs);
		}

		void CloseDoor()
		{
			// Allow force-open button to keep the door open, but only for a maximum of c_doorRemainsOpenMaxSecs.
			DateTime started = DateTime.Now;
			while (m_openDoorForce && DateTime.Now.Subtract(started).TotalSeconds < c_doorRemainsOpenMaxSecs)
				Thread.Sleep(c_doorPollIntervalMsecs);

			if (m_openDoorForce)
				Console.WriteLine("{0:mm:ss} - {1}.{2}: ALARM! Force door timer exceeded!", DateTime.Now, m_id, m_currFloor);

			Console.WriteLine("{0:mm:ss} - {1}.{2}: close door", DateTime.Now, m_id, m_currFloor);
		}

		bool MoveElevator()
		{
			if (AtTopOrBottom())	// Flip direction if at top or bottom
			{
				lock (m_lockObj)
				{
					m_direction = (m_direction == Direction.Up) ? Direction.Down : Direction.Up;
					Console.WriteLine("{0:mm:ss} - {1}.{2}: switched direction, going {3}", DateTime.Now, m_id, m_currFloor, m_direction);
				}
			}
			else					// Else move up/down
			{
				lock (m_lockObj)
					m_currFloor += (m_direction == Direction.Up ? 1 : -1);

				Thread.Sleep(c_moveTimePerFloorSecs * 1000);
			}

			return FloorsRemain();	// Return true if there are still marked floors
		}

		bool AtTopOrBottom()
		{
			// Returns true if at top/bottom physical floor.  Also returns true if at top/bottom
			// marked floor in either direction, or if no floors remain marked.

			if (m_currFloor == 0 || m_currFloor == m_numFloors - 1)
				return true;
			
			if (GetMarkedTopAndBottom(out int topMarked, out int bottomMarked))
				return (m_currFloor >= topMarked && m_direction == Direction.Up) || (m_currFloor <= bottomMarked && m_direction == Direction.Down);
			else
				return true;
		}

		// Return the closest marked floor, if any.
		bool FindClosestMarkedFloor(out int closestFloor)
		{
			closestFloor = -1;
			int closestFloorDist = int.MaxValue;

			lock (m_lockObj)
			{
				for (int i = 0; i < m_numFloors; i++)
				{
					if ((m_floorsUp[i] || m_floorsDown[i]) && Math.Abs(i - m_currFloor) < closestFloorDist)
					{
						closestFloor = i;
						closestFloorDist = Math.Abs(i - m_currFloor);
					}
				}
			}

			return closestFloor != -1;
		}

		// Are there any marked floors left in the current direction?
		bool FloorsRemain()
		{
			lock (m_lockObj)
			{
				int increment = (m_direction == Direction.Up) ? 1 : -1;
				for (int i = m_currFloor; i >= 0 && i < m_numFloors; i += increment)
					if (m_floorsUp[i] || m_floorsDown[i]) return true;
			}

			return false;
		}

		// Get the top/bottom marked floors.
		bool GetMarkedTopAndBottom(out int topMarked, out int bottomMarked)
		{
			bool hasMarked = false;
			topMarked = int.MinValue;
			bottomMarked = int.MaxValue;

			lock (m_lockObj)
			{
				for (int i = 0; i < m_numFloors; i++)
				{
					if (m_floorsUp[i] || m_floorsDown[i])
					{
						hasMarked = true;
						if (i < bottomMarked) bottomMarked = i;
						if (i > topMarked) topMarked = i;
					}
				}
			}

			return hasMarked;
		}
	}
}
