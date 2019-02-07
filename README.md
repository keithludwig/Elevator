ELEVATOR
--------

This C# project simulates a bank of elevators and riders.  It is governed by an input file
that controls the simulation -- with commands to initialize the system and add riders with
starting and ending floors. The system runs multiple threads -- one for processing the 
command file, another for the controller, and one for each elevator in the system.

The ElevatorController class runs the bank of elevators, and keeps a queue of rider requests.
To service a request, ElevatorController attempts to find the best elevator for the task,
as dictated by FindBestElevator.  This method identifies the nearest idle elevator, if one
is available.  It also finds the nearest elevator that is going in the right direction, and
is above/below the requesting floor, as necessary.  Then it picks whichever of these two is
closest to the requesting floor.  If no suitable elevators are found, the request remains in
the queue until it can be processed.

The Elevator class contains the logic for a single elevator.  It has 3 states -- idle, moving,
and visiting floor -- and has a current direction -- up, down, or none.  It starts off idle and
waits for a signal from the elevator controller.  Then it chooses a starting direction based on
the closest marked floor.  Once it moves in a given direction, it will handle all requests in
that direction before switching and handling requests in the other direction.

It also has logic for forcing the door to close (when open), or to remain open (when closing).
There is no easy way to test this with the current command file capabilities, but the logic
should be sound (and includes a timer that expires after 60 seconds when forcing the door open).
