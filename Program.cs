using System;
using System.IO;
using System.Threading;

namespace Elevator
{
	class Program
	{
		static void Main(string[] args)
		{
			// This code reads input commands from a text file and passes them along to an
			// elevator controller object.
			string path = args.Length > 0 ? args[0] : "..\\..\\ElevatorInput.txt";
			string[] lines = File.ReadAllLines(path);

			ElevatorController ctrl = new ElevatorController();

			for (int i = 0; i < lines.Length; i++)
			{
				string[] cmd = lines[i].ToLower().Split(" ".ToCharArray());
				
				try
				{
					if (cmd[0] == "init")				// init {numElevs} {numFloors}				- Initializes the ElevatorController
					{
						ctrl.Start(int.Parse(cmd[1]), int.Parse(cmd[2]));
					}
					else if (cmd[0] == "sleep")			// sleep {numSecs}							- sleeps this thread for numSecs
					{
						Thread.Sleep(int.Parse(cmd[1]) * 1000);
					}
					else if (cmd[0] == "rider")			// rider {name} {startFloor} {destFloor}	- submits a request for a rider
					{
						Rider rider		= new Rider() { m_name = cmd[1], m_destFloor = int.Parse(cmd[3]) };
						int startFloor	= int.Parse(cmd[2]);

						ctrl.Floors[startFloor].AddRider(rider);
						Elevator.Direction dir = rider.m_destFloor > startFloor ? Elevator.Direction.Up : Elevator.Direction.Down;
						ctrl.RequestElevator(startFloor, dir);
					}
					else if (cmd[0] == "quit")			// quit the app
					{
						break;
					}
					else
					{
						// Ignore all other input (like comments)
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine("{0:mm:ss} - Exception on line {1}: {2}", DateTime.Now, i, ex);
				}
			}

			ctrl.Stop();
		}
	}
}
