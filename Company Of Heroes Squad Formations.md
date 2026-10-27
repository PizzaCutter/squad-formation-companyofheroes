pp
# Squad Makeup
Each squad is broken up into three elements: core, left flank, and right flank. Each element has a leader, and the leader of the core element is the leader of the squad. Roles are assigned to soldiers and updated whenever one is killed or the squad is reinforced.
## Move Out
When a move order is assigned the leader immediately computes a path and starts moving. To generate goals for other squad members we predict the future position of the leader along his path roughly two seconds ahead. Then we take an offset from the leader's future position and orient it using the leaders' future heading to generate goals for element leaders. The followers of those element leaders then do the same. Sometimes units fall behind and others pull ahead. To fix this we introduce a speed modifier by taking the formation offset we calculated and apply it relative to the leader's current position instead of the future position.
# Softening the shape
If the offset goal is in an unreachable location this causes issues. The way this was solved is by using A* pathfind in a short radius around the leader location. This will give us some nearest position that is also "easily" reachable from the leader position. 
On top of this there is also some personal drift stored that is added to its formation offset for all the formation calculations. Each simulation tick the drift value floats around via a random offset within a maximum radius to make the formation feel less robotic.
# Formation Context
Not all formations are appropriate for all situations. When a wide-open field we want broad wedge shape but in cramped city streets we might want a tight wedge. To fix this the formation used is based on the terrain the squad leader is passing over
# Leapfrogging
Leapfrogging means that one of the elements is going to leave his normal place in the formation, run to some interesting spot in the world, hold for some amount of time and then resume moving in formation. Only element leaders perform this behavior, and element followers are simply along for the ride. There can also be conditional leapfrogging such as soft leapfrogging when retreating or under fire to still do the requested move but visit for example cover and slowing down near cover but to not stop moving
# Virtual Leader
Leapfrogging introduces a new problem where the core element stops heading for the goal and heads for some cover which would make the entire squad veer off. This is solved by having a virtual leader during leapfrogging that the squad follows. Once the leader is back aligned we then remove the virtual leader again. 
# Destination Formation
A separate formation is used when a squad gets near its destination. Here each soldier marks his destination spot with a reservation to make sure that no one else is standing there when he arrives and no one else is allowed to use that spot as a destination until he gives it up. The user sets the facing formation by right clicking but if no formation face is given we help the player by facing enemies. Each solider also does a area search for cover and goes there instead. With ofc the leader being prioritized. 
# Handling destruction
Because the leader recalculates it's path every couple of ticks any changes to the navmesh automatically reflects movement adjustments for the entire squad so no special logic is needed. 

