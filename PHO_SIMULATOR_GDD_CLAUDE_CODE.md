# PHỞ SIMULATOR
## Master Game Design Document + Claude Code Development Prompt

**Working title:** Phở Simulator  
**Genre:** First-person restaurant simulator / life simulation / business management  
**Perspective:** Primarily first-person, with optional management UI/map views  
**Target:** PC first, Steam-oriented indie release  
**Recommended engine:** Unity 6 + C#  
**Alternative:** Godot 4.x + C# or GDScript  
**Tone:** Cozy, humorous, grounded, slightly chaotic, highly systemic  
**Core fantasy:** Start with almost nothing, cook bowls of phở by hand, survive each day, build a reputation, hire staff, expand the restaurant, automate operations, and eventually build a recognizable phở empire.

---

# 1. HIGH-CONCEPT PITCH

**Phở Simulator** is a first-person restaurant and life-management simulator where the player starts with a tiny rented food stall, a small amount of money, basic cooking equipment, and a family phở recipe.

During the day, the player physically performs restaurant work:

- Shop for ingredients.
- Prepare broth.
- Slice meat.
- Wash herbs.
- Assemble bowls.
- Serve customers.
- Clean tables.
- Handle payments.
- Manage deliveries.
- Repair equipment.
- Deal with rush hours and unexpected events.

Outside active service, the player manages the business:

- Set menu prices.
- Upgrade equipment.
- Renovate the restaurant.
- Hire and train staff.
- Negotiate with suppliers.
- Unlock new recipes.
- Advertise.
- Manage customer reviews.
- Expand seating capacity.
- Open additional branches.
- Improve the player's home and lifestyle.
- Build relationships with recurring NPCs.

The game should feel like a progression from:

> "I personally do everything in a tiny phở stall"

to:

> "I manage a highly optimized restaurant with staff, reputation, loyal customers, suppliers, delivery orders, and multiple revenue streams."

The game should prioritize **emergent stories** over scripted missions.

---

# 2. DESIGN PILLARS

## 2.1 Hands-On Work

The early game should make the player physically interact with restaurant tasks.

Examples:

- Carry ingredient boxes.
- Fill pots.
- Turn burners on.
- Stir broth.
- Add ingredients.
- Slice meat.
- Portion noodles.
- Arrange herbs.
- Carry bowls to customers.
- Clean dirty tables.

The player should feel that they are actually operating a restaurant rather than only navigating menus.

---

## 2.2 Meaningful Progression

Nearly every repetitive activity should eventually become:

1. Faster.
2. Easier.
3. Automatable.
4. Delegatable.

Examples:

| Early Game | Mid Game | Late Game |
|---|---|---|
| Player washes every bowl | Buy industrial sink | Hire dishwasher |
| Player slices meat manually | Buy better knife/slicer | Prep cook handles it |
| Player takes every order | Order terminal | Waiter/cashier handles it |
| Player shops manually | Supplier delivery | Automatic procurement |
| Player cooks one broth pot | Larger stock pot | Multiple automated stations |

Progression must produce visible changes in the restaurant.

---

## 2.3 Controlled Chaos

The restaurant should generate interesting operational problems.

Examples:

- Lunch rush.
- Ingredient shortage.
- Burner breaks.
- Customer complains.
- Online review goes viral.
- Supplier delivers late.
- Employee calls in sick.
- Group of ten customers arrives.
- Influencer visits unexpectedly.
- Health inspector arrives.
- Rain reduces foot traffic.
- Festival increases demand.
- Power outage.
- Delivery driver waits too long.

These systems generate stories without needing complex story missions.

---

## 2.4 Vietnamese Identity

The game should have a strong Vietnamese food and street-business identity without becoming a caricature.

Important elements:

- Authentic phở preparation concepts.
- Herbs and condiments.
- Vietnamese shop architecture.
- Sidewalk seating.
- Motorbike delivery.
- Coffee and drinks.
- Local street ambience.
- Supplier markets.
- Vietnamese signage.
- Family-owned restaurant themes.
- Regional recipe variation.

The world can be fictional but inspired by Vietnamese cities.

---

## 2.5 Player Expression

Players should be able to create "their" restaurant.

Customization should include:

- Restaurant name.
- Sign.
- Logo.
- Wall color.
- Furniture.
- Lighting.
- Decorations.
- Bowl style.
- Uniforms.
- Menu design.
- Music.
- Layout.
- Kitchen layout.
- Seating arrangement.

The restaurant should visually evolve throughout the game.

---

# 3. GAME STRUCTURE

The game uses an open-ended sandbox structure.

Typical gameplay:

```text
Wake up
↓
Check money / messages / inventory
↓
Buy or receive ingredients
↓
Prepare restaurant
↓
Cook broth / prep ingredients
↓
Open restaurant
↓
Handle customers
↓
Lunch rush
↓
Restock / clean
↓
Dinner rush
↓
Close restaurant
↓
Clean / calculate profit
↓
Upgrade / shop / renovate
↓
Sleep
↓
Next day
```

The player controls when the restaurant opens and closes.

---

# 4. PLAYER STARTING CONDITION

The player begins with:

- Small rented restaurant.
- 4 tables.
- 8–12 seats.
- Small kitchen.
- One broth pot.
- One burner.
- One refrigerator.
- One sink.
- One cutting board.
- Cheap knife.
- Basic bowls.
- Small amount of starting cash.
- One basic phở recipe.
- No employees.
- Low reputation.
- Few regular customers.
- Limited supplier options.

Example starting money:

```text
Cash: $1,500 equivalent
Rent due: 7 days
Restaurant reputation: 0 / 100
Daily customer expectation: 5–12
```

The first objective is simply:

> Survive the first week and pay rent.

---

# 5. CORE GAMEPLAY LOOP

The core loop should operate at three time scales.

## Moment-to-Moment Loop

```text
Receive order
→ Prepare bowl
→ Serve customer
→ Customer evaluates food/service
→ Receive payment/tip/reputation
```

## Daily Loop

```text
Purchase ingredients
→ Prepare stock
→ Open restaurant
→ Serve customers
→ Manage problems
→ Close restaurant
→ Calculate profit
```

## Long-Term Loop

```text
Earn money
→ Upgrade restaurant
→ Serve more customers
→ Increase reputation
→ Unlock recipes/customers
→ Hire staff
→ Increase automation
→ Expand restaurant
```

---

# 6. PHỞ COOKING SYSTEM

Cooking must be simple enough to play repeatedly but deep enough to reward mastery.

## 6.1 Broth

Broth is the foundation of the product.

Possible broth parameters:

```text
Water
Beef bones
Onion
Ginger
Star anise
Cinnamon
Clove
Fish sauce
Salt
Rock sugar
Cooking duration
Temperature
Ingredient quality
```

The player does not need realistic multi-hour waiting every time.

Game time can compress cooking.

Example:

```text
Real-world broth:
6–12 hours

Game broth:
5–15 minutes depending on equipment
```

Broth quality calculation:

```text
BrothQuality =
IngredientQuality
+ RecipeAccuracy
+ CookingTimeAccuracy
+ TemperatureControl
+ PlayerSkill
```

Quality range:

```text
0–100
```

Example ratings:

```text
0–30   Bad
31–50  Cheap
51–70  Good
71–85  Excellent
86–100 Legendary
```

---

# 7. BOWL ASSEMBLY SYSTEM

Each customer order contains preferences.

Example order:

```json
{
  "dish": "Pho Tai",
  "size": "Large",
  "no_onion": true,
  "extra_meat": true,
  "spice": "medium"
}
```

Player assembly:

```text
Bowl
→ noodles
→ meat
→ broth
→ onion
→ herbs
→ optional toppings
```

Mistakes affect satisfaction.

Examples:

- Wrong size.
- Missing meat.
- Too little broth.
- Wrong toppings.
- Cold broth.
- Long waiting time.

---

# 8. MENU SYSTEM

Initial menu:

- Phở Tái
- Water
- Soft drink

Unlockable:

### Phở

- Phở Tái
- Phở Chín
- Phở Nạm
- Phở Gầu
- Phở Bò Viên
- Phở Đặc Biệt
- Phở Gà

### Side Dishes

- Quẩy
- Beef meatballs
- Extra meat
- Extra noodles

### Drinks

- Trà đá
- Vietnamese iced coffee
- Soft drinks
- Fresh juice

### Optional Late-Game Expansion

- Bún bò
- Cơm dishes
- Spring rolls

However, phở must remain the primary identity of the game.

---

# 9. INGREDIENT SYSTEM

Every ingredient has:

```text
Name
Category
Quality
Freshness
Purchase cost
Storage requirement
Shelf life
Supplier
```

Example:

```json
{
  "name": "Beef Brisket",
  "quality": 78,
  "freshness": 92,
  "cost_per_kg": 11.5,
  "storage": "refrigerated"
}
```

Ingredient quality affects food quality.

Freshness decreases over time.

Spoiled ingredients:

- Lower food quality.
- May trigger customer complaints.
- May create health-inspection penalties.

---

# 10. SUPPLIER SYSTEM

The player initially buys supplies manually.

Example locations:

- Wet market.
- Supermarket.
- Meat supplier.
- Produce supplier.
- Wholesale store.

Later, suppliers offer delivery.

Supplier stats:

```text
Price
Quality
Reliability
Delivery speed
Minimum order
Relationship
```

Player can build supplier relationships.

Benefits:

- Discounts.
- Better ingredients.
- Priority deliveries.
- Credit terms.
- Exclusive ingredients.

---

# 11. CUSTOMER SYSTEM

Customers are simulated NPCs with needs and preferences.

Each customer has:

```text
Budget
Patience
Food quality expectation
Cleanliness sensitivity
Service sensitivity
Favorite dishes
Spice preference
Personality type
Tip probability
Review probability
```

Example customer archetypes:

- Student.
- Office worker.
- Construction worker.
- Tourist.
- Food enthusiast.
- Family.
- Elderly local.
- Delivery customer.
- Influencer.
- Food critic.

---

# 12. CUSTOMER SATISFACTION

Example formula:

```text
Satisfaction =
FoodQuality * 0.40
+ WaitTimeScore * 0.20
+ ServiceScore * 0.15
+ Cleanliness * 0.15
+ PriceValue * 0.10
```

Satisfaction range:

```text
0–100
```

Results:

```text
0–30   Angry
31–50  Unhappy
51–70  Satisfied
71–90  Happy
91–100 Fan
```

Possible actions:

- Leave tip.
- Return another day.
- Recommend friends.
- Leave online review.
- Complain.
- Ask for refund.

---

# 13. REPUTATION SYSTEM

Restaurant reputation:

```text
0–100
```

Affected by:

- Reviews.
- Customer satisfaction.
- Cleanliness.
- Food consistency.
- Prices.
- Social media.
- Special events.

Reputation tiers:

```text
0–10    Unknown Stall
11–25   Neighborhood Shop
26–45   Local Favorite
46–65   Popular Restaurant
66–80   City Favorite
81–95   Famous Restaurant
96–100  Legendary Phở
```

Higher reputation:

- More customers.
- More demanding customers.
- Influencers.
- Critics.
- Tourists.
- Higher price tolerance.

---

# 14. ONLINE REVIEW SYSTEM

Customers may leave reviews after visits.

Example:

```text
★★★★★
"Broth was incredible, but the restaurant was very dirty."
```

Reviews can mention:

- Taste.
- Wait time.
- Price.
- Cleanliness.
- Service.
- Portion size.

Average rating displayed:

```text
4.2 / 5
```

Online rating affects future customer traffic.

---

# 15. RESTAURANT CLEANLINESS

Restaurant surfaces accumulate dirt.

Dirty systems:

- Tables.
- Floor.
- Kitchen counter.
- Stove.
- Sink.
- Trash bins.
- Bathroom.

Player must:

- Wipe tables.
- Mop floor.
- Wash dishes.
- Empty trash.
- Clean kitchen.

Dirty environments reduce:

- Customer satisfaction.
- Staff morale.
- Health inspection rating.

---

# 16. HEALTH INSPECTION SYSTEM

Random health inspections can occur after the early tutorial period.

Inspector checks:

- Spoiled food.
- Kitchen cleanliness.
- Trash.
- Sink hygiene.
- Food storage.
- Pests.
- Bathroom cleanliness.

Inspection result:

```text
A
B
C
Fail
```

Failure can cause:

- Fine.
- Reputation loss.
- Temporary closure.

---

# 17. DAY / NIGHT CYCLE

Example in-game day:

```text
06:00 Wake
07:00 Preparation
09:00 Breakfast customers
11:30 Lunch rush
14:00 Quiet period
17:30 Dinner rush
21:00 Closing
23:00 Late activities
```

Player can customize business hours.

Longer hours may generate more revenue but increase:

- Fatigue.
- Staff salary.
- Ingredient use.
- Cleaning requirements.

---

# 18. PLAYER ENERGY

Optional system.

Player energy:

```text
0–100
```

Activities reduce energy:

- Running.
- Carrying heavy boxes.
- Cooking.
- Cleaning.

Energy restored by:

- Eating.
- Coffee.
- Rest.
- Sleeping.

Important:

Energy should create pacing, not annoyance.

It should become less important once staff are hired.

---

# 19. STAFF SYSTEM

Employee roles:

- Cashier.
- Waiter.
- Dishwasher.
- Prep cook.
- Cook.
- Cleaner.
- Manager.
- Delivery coordinator.

Employee attributes:

```text
Speed
Skill
Accuracy
Reliability
Stress tolerance
Salary
Experience
Morale
```

Example employee:

```json
{
  "name": "Minh",
  "role": "Waiter",
  "speed": 71,
  "accuracy": 64,
  "reliability": 82,
  "salary": 60
}
```

Employees improve through experience.

---

# 20. STAFF AI

Staff uses task priority logic.

Example waiter priority:

```text
1. Deliver finished food
2. Take new orders
3. Clear dirty tables
4. Refill drinks
```

Cook:

```text
1. Complete active order
2. Refill noodles
3. Prepare ingredients
4. Clean station
```

Manager can automate:

- Ordering supplies.
- Scheduling employees.
- Restaurant opening.
- Closing procedures.

---

# 21. EMPLOYEE PERSONALITY

Employees can have traits.

Positive:

- Hard Worker.
- Friendly.
- Fast Learner.
- Perfectionist.
- Loyal.

Negative:

- Lazy.
- Late.
- Clumsy.
- Easily Stressed.
- Argumentative.

These traits create emergent stories.

---

# 22. RESTAURANT EXPANSION

The restaurant progresses through stages.

## Stage 1 — Street Stall

- 4 tables.
- Player handles everything.

## Stage 2 — Small Restaurant

- 8–12 tables.
- First employees.
- Supplier delivery.

## Stage 3 — Popular Restaurant

- Large kitchen.
- Multiple cooking stations.
- Delivery orders.

## Stage 4 — Premium Restaurant

- Interior customization.
- Large staff.
- Tourist/customer events.

## Stage 5 — Second Location

Player can buy or lease another restaurant.

---

# 23. RESTAURANT BUILDING MODE

Players can customize layout.

Buildable objects:

### Kitchen

- Burner.
- Broth pot.
- Prep table.
- Refrigerator.
- Freezer.
- Sink.
- Dishwasher.
- Meat slicer.
- Noodle station.

### Dining

- Tables.
- Chairs.
- Counter.
- Decorations.
- Plants.
- Lighting.

### Utility

- Trash bins.
- Storage shelves.
- Fans.
- Air conditioning.
- Bathroom fixtures.

Placement should use grid snapping with optional free placement.

---

# 24. EQUIPMENT UPGRADES

Equipment creates operational progression.

Example burner:

```text
Cheap Burner
Heat Speed: 1.0
Efficiency: 1.0

Commercial Burner
Heat Speed: 1.5
Efficiency: 1.2

Industrial Burner
Heat Speed: 2.0
Efficiency: 1.5
```

Example dishwasher:

```text
Manual Sink
8 seconds / bowl

Small Dishwasher
10 bowls / 30 seconds

Industrial Dishwasher
25 bowls / 20 seconds
```

---

# 25. ECONOMY

Main expenses:

```text
Rent
Ingredients
Utilities
Employee salaries
Equipment
Furniture
Repairs
Advertising
Taxes
Delivery fees
```

Revenue:

```text
Restaurant food sales
Delivery orders
Catering
Special events
Merchandise (late game)
```

Daily financial summary:

```text
Revenue:       $1,850
Ingredients:   -$530
Salaries:      -$310
Utilities:     -$90
Other costs:   -$70
--------------------
Profit:         $850
```

---

# 26. MENU PRICING

Player sets menu prices.

Example:

```text
Pho Tai

Ingredient cost: $3.20
Current price: $9.50
Recommended: $9–11
```

Too cheap:

- High traffic.
- Low margin.

Too expensive:

- Fewer customers.
- More complaints.

Customer price tolerance depends on:

- Reputation.
- Restaurant appearance.
- Food quality.

---

# 27. RANDOM EVENTS

Random events should create operational variety.

Examples:

### Positive

- Local influencer visit.
- Food blogger review.
- Festival increases traffic.
- Supplier discount.
- Celebrity visit.
- Viral social media post.

### Negative

- Refrigerator failure.
- Water outage.
- Power outage.
- Staff absence.
- Supplier delay.
- Ingredient price spike.
- Health inspection.
- Angry customer.
- Rainstorm.
- Road closure.

---

# 28. WEATHER SYSTEM

Weather affects customer traffic.

Example:

```text
Sunny:
+10% walk-in customers

Heavy Rain:
-25% walk-in
+30% delivery

Cold Weather:
+15% phở demand
```

Weather can also affect ambience.

---

# 29. DELIVERY ORDERS

Mid-game feature.

Sources:

- Phone orders.
- Delivery app.
- Online ordering.

Delivery order gameplay:

```text
Order arrives
→ Kitchen prepares
→ Package food
→ Driver arrives
→ Hand order to driver
```

Late delivery reduces rating.

Players can create a dedicated delivery station.

---

# 30. LIFE SIMULATION

The player should have a simple life outside the restaurant.

This should support progression without overshadowing the restaurant.

Possible systems:

- Apartment.
- Sleeping.
- Eating.
- Clothing.
- Transportation.
- Relationships.
- Personal purchases.

Player upgrades:

```text
Tiny room
→ Apartment
→ Nice apartment
→ House
```

Vehicle progression:

```text
Walking
→ Bicycle
→ Used motorbike
→ Better motorbike
→ Car
```

Vehicles help transport ingredients.

---

# 31. NPC RELATIONSHIPS

Recurring NPCs include:

- Landlord.
- Ingredient supplier.
- Butcher.
- Vegetable vendor.
- Neighbor restaurant owner.
- Regular customer.
- Food critic.
- Delivery app representative.

Relationships can unlock:

- Better deals.
- Story events.
- Recipes.
- Discounts.

---

# 32. COMPETITOR RESTAURANTS

Nearby restaurants exist.

Competitors have:

```text
Price
Quality
Reputation
Specialty
Customer capacity
```

Competition affects local demand.

Possible actions:

- Improve quality.
- Advertise.
- Offer specials.
- Expand menu.

Do NOT include sabotage mechanics in the initial version.

---

# 33. SKILL SYSTEM

Player skills:

```text
Cooking
Knife Skill
Business
Customer Service
Cleaning
Management
```

Example:

Cooking level improves:

- Food consistency.
- Preparation speed.
- Quality tolerance window.

Management improves:

- Employee efficiency.
- Hiring information.
- Scheduling.

---

# 34. RECIPE DISCOVERY

Recipes can be unlocked via:

- Skill progression.
- NPC relationships.
- Recipe books.
- Restaurant milestones.
- Experimentation.

Recipe variations:

- Northern-style inspiration.
- Southern-style inspiration.
- Chicken phở.
- Special house recipe.

Avoid declaring one style "correct."

---

# 35. TUTORIAL

Tutorial should be integrated into the first day.

Day 1 objectives:

```text
Buy ingredients
Cook first broth
Prepare noodles
Open restaurant
Serve 3 customers
Clean restaurant
Close shop
```

Tutorial NPC can be:

- Relative.
- Former owner.
- Friendly neighbor.

Avoid excessive text tutorials.

Use contextual tooltips.

---

# 36. FIRST 7 DAYS

## Day 1
Learn restaurant basics.

## Day 2
More customers.

## Day 3
Ingredient freshness introduced.

## Day 4
Online reviews introduced.

## Day 5
Lunch rush introduced.

## Day 6
Equipment upgrade opportunity.

## Day 7
Rent payment.

Goal:

```text
Pay rent and remain profitable.
```

---

# 37. MID-GAME

Expected player state:

- 8–15 tables.
- 3–6 employees.
- 8–12 menu items.
- Supplier deliveries.
- Delivery app.
- Strong reputation.
- Restaurant customization.
- Equipment automation.

Player begins focusing on optimization.

---

# 38. LATE GAME

Late game becomes business management.

Features:

- Multiple locations.
- Managers.
- Central ingredient procurement.
- Restaurant branding.
- Advanced employees.
- Events.
- Premium ingredients.
- Large delivery business.

Player spends less time physically cooking unless they choose to.

---

# 39. OPTIONAL FRANCHISE SYSTEM

Post-MVP.

Player can operate multiple branches.

Each branch tracks:

```text
Revenue
Expenses
Manager
Reputation
Staff
Inventory
Menu
```

Managers can automate locations.

---

# 40. ART DIRECTION

Recommended visual style:

**Stylized realism.**

Reference qualities:

- Warm lighting.
- Slightly exaggerated food visuals.
- Readable objects.
- Detailed but optimized environments.

Avoid photorealism because it significantly increases production cost.

Food should be visually appealing.

Broth needs:

- Steam.
- Surface reflections.
- Ingredient visibility.

---

# 41. WORLD DESIGN

Recommended MVP map:

```text
Player restaurant
Player apartment
Street
Wet market
Supermarket
Equipment store
Furniture store
Supplier warehouse
```

Map should be compact.

Traversal should be fast.

Avoid building a huge open world.

---

# 42. AUDIO

Important audio elements:

Kitchen:

- Boiling broth.
- Knife chopping.
- Plates.
- Sink water.
- Frying.
- Refrigerator hum.

Restaurant:

- Customer chatter.
- Chairs.
- Cash register.
- Street traffic.

Ambient:

- Motorbikes.
- Rain.
- Street vendors.

Music should be subtle.

---

# 43. UI

Main HUD:

```text
Cash
Current time
Current orders
Restaurant status
Player energy
Reputation
```

Order UI:

```text
Table 3

2x Pho Tai
1x Pho Chin
No onion
Extra meat
```

Management tablet/computer:

```text
Dashboard
Menu
Employees
Inventory
Suppliers
Reviews
Finances
Upgrades
```

---

# 44. INVENTORY SYSTEM

Inventory categories:

```text
Ingredients
Drinks
Cleaning supplies
Packaging
Equipment
Furniture
```

Inventory objects can exist physically in storage.

Example:

```text
Box of noodles
20 portions
```

Players can carry boxes and place them on shelves.

---

# 45. PHYSICAL ITEM SYSTEM

Important objects should exist physically in the world.

Examples:

- Ingredient containers.
- Bowls.
- Food plates.
- Boxes.
- Cleaning tools.
- Trash bags.

Player interaction:

```text
Look at object
→ highlight
→ interact
→ pick up/use/place
```

This creates immersion.

---

# 46. INTERACTION SYSTEM

Use a raycast-based first-person interaction system.

Interface:

```csharp
public interface IInteractable
{
    string GetInteractionText();
    void Interact(PlayerInteractor player);
}
```

Common interactables:

```text
Door
Stove
Pot
IngredientContainer
Table
Chair
CashRegister
Sink
TrashBin
NPC
Shelf
```

---

# 47. ORDER STATE MACHINE

```text
Created
→ Waiting
→ Accepted
→ Preparing
→ Ready
→ Served
→ Completed
```

Failure states:

```text
Cancelled
Refunded
Expired
```

---

# 48. CUSTOMER STATE MACHINE

```text
Spawn
→ EnterRestaurant
→ FindTable
→ Sit
→ BrowseMenu
→ Order
→ WaitForFood
→ Eat
→ Pay
→ Leave
```

Possible alternate path:

```text
WaitTooLong
→ BecomeAngry
→ Leave
```

---

# 49. NPC NAVIGATION

Recommended:

Unity NavMesh.

NPC requirements:

- Find entrance.
- Queue.
- Find free table.
- Sit.
- Walk to cashier if needed.
- Exit restaurant.

Avoid complex crowd simulation for MVP.

Target maximum initial NPC count:

```text
20–30 active NPCs.
```

---

# 50. SAVE SYSTEM

Save:

```text
Player money
Player position
Day/time
Restaurant layout
Inventory
Equipment
Menu
Reputation
Employees
Supplier relationships
Recipes
Skill levels
Quest/tutorial progress
```

Recommended format:

```text
JSON during development
Binary/encrypted serialization later if necessary
```

Use versioned save files.

---

# 51. DATA-DRIVEN ARCHITECTURE

Game configuration should be data-driven.

Unity recommendation:

```text
ScriptableObjects
```

Examples:

```text
IngredientData
RecipeData
EquipmentData
CustomerArchetype
EmployeeData
FurnitureData
EventData
```

Runtime state should be separate from static definition data.

---

# 52. RECOMMENDED UNITY PROJECT ARCHITECTURE

```text
Assets/
  Art/
  Audio/
  Materials/
  Prefabs/
  Scenes/
  Scripts/
    Core/
    Player/
    Interaction/
    Cooking/
    Restaurant/
    Customers/
    Employees/
    Economy/
    Inventory/
    Building/
    UI/
    Save/
    World/
    Events/
  ScriptableObjects/
  Resources/
```

---

# 53. IMPORTANT SOFTWARE SYSTEMS

Core managers:

```text
GameManager
TimeManager
SaveManager
EconomyManager
RestaurantManager
OrderManager
CustomerManager
InventoryManager
EmployeeManager
EventManager
AudioManager
UIManager
```

Avoid turning all managers into global singletons.

Use dependency references or service registration where practical.

---

# 54. MVP SCOPE

The first playable MVP should contain ONLY:

```text
1 restaurant
1 street
1 supplier/store
3 phở recipes
1 drink
5 customer archetypes
basic cooking
basic ordering
basic payments
cleaning
inventory
restaurant upgrades
day/night cycle
save/load
simple reputation
```

No:

```text
Multiple restaurants
Complex relationships
Franchising
Large open world
Advanced employees
Online multiplayer
```

---

# 55. VERTICAL SLICE GOAL

The vertical slice should allow:

1. Player starts a new game.
2. Player buys ingredients.
3. Player returns to restaurant.
4. Player prepares ingredients.
5. Player cooks broth.
6. Player opens restaurant.
7. Customers spawn.
8. Customers order.
9. Player cooks and serves bowls.
10. Customers pay.
11. Player earns money.
12. Player cleans.
13. Player closes restaurant.
14. Player buys one upgrade.
15. Game saves.
16. Next day begins.

If this loop is fun, continue development.

---

# 56. DEVELOPMENT ROADMAP

## Phase 0 — Project Foundation

Implement:

- Unity project.
- Git repository.
- Folder architecture.
- Core game state.
- Test scene.

---

## Phase 1 — First-Person Controller

Implement:

- Movement.
- Mouse look.
- Sprint.
- Interaction raycast.
- Pick up objects.
- Place objects.

---

## Phase 2 — Restaurant Interaction

Implement:

- Tables.
- Chairs.
- Doors.
- Kitchen stations.
- Item storage.

---

## Phase 3 — Cooking

Implement:

- Ingredient containers.
- Broth pot.
- Noodles.
- Meat.
- Bowl assembly.
- Food quality.

---

## Phase 4 — Customers

Implement:

- NPC spawning.
- NavMesh.
- Table selection.
- Order generation.
- Eating.
- Payment.
- Leaving.

---

## Phase 5 — Economy

Implement:

- Cash.
- Ingredient costs.
- Sales.
- Daily financial report.
- Rent.

---

## Phase 6 — Inventory & Suppliers

Implement:

- Ingredient inventory.
- Shopping.
- Supplier UI.
- Ingredient delivery.

---

## Phase 7 — Reputation

Implement:

- Satisfaction.
- Reviews.
- Customer traffic scaling.

---

## Phase 8 — Employees

Implement:

- Hiring.
- Employee AI.
- Task system.
- Salaries.

---

## Phase 9 — Building Mode

Implement:

- Furniture placement.
- Equipment placement.
- Restaurant customization.

---

## Phase 10 — Polish

Implement:

- Sound.
- Effects.
- Animations.
- Better UI.
- Optimization.
- Steam integration later.

---

# 57. TECHNICAL PRINCIPLES FOR CLAUDE CODE

Claude Code must follow these rules.

## Code Quality

- Use clean C#.
- Prefer composition over deep inheritance.
- Use interfaces for interactive systems.
- Avoid God classes.
- Avoid unnecessary singletons.
- Separate data from runtime state.
- Use events for decoupled systems.
- Add XML comments only where useful.
- Keep classes focused.
- Avoid premature optimization.

---

# 58. TESTING REQUIREMENTS

Use automated tests where practical.

Priority test targets:

```text
Economy calculations
Recipe scoring
Customer satisfaction
Order state transitions
Inventory transactions
Save serialization
Employee task priorities
```

Gameplay scene behavior can use integration tests where feasible.

---

# 59. GAME BALANCE CONFIG

Never hardcode balance constants inside gameplay logic.

Example:

Bad:

```csharp
customer.Patience -= 5f;
```

Preferred:

```csharp
customer.Patience -= customerConfig.patienceDecayRate * deltaTime;
```

Balance data should live in config assets.

---

# 60. SAMPLE RECIPE DATA MODEL

```csharp
[CreateAssetMenu(menuName = "PhoSimulator/Recipe")]
public class RecipeData : ScriptableObject
{
    public string recipeId;
    public string displayName;

    public List<RecipeIngredientRequirement> ingredients;

    public float basePrice;
    public float preparationDifficulty;
}
```

---

# 61. SAMPLE INGREDIENT MODEL

```csharp
[CreateAssetMenu(menuName = "PhoSimulator/Ingredient")]
public class IngredientData : ScriptableObject
{
    public string ingredientId;
    public string displayName;

    public float purchasePrice;
    public float shelfLifeHours;

    public IngredientCategory category;
}
```

Runtime stack:

```csharp
[System.Serializable]
public class IngredientStack
{
    public IngredientData ingredient;
    public int quantity;
    public float freshness;
}
```

---

# 62. SAMPLE ORDER MODEL

```csharp
public class RestaurantOrder
{
    public string OrderId;
    public Customer Customer;
    public List<OrderItem> Items;

    public OrderState State;

    public float CreatedTime;
    public float TotalPrice;
}
```

---

# 63. TASK SYSTEM FOR EMPLOYEE AI

Use shared restaurant tasks.

Example:

```csharp
public interface IRestaurantTask
{
    int Priority { get; }

    bool CanExecute(Employee employee);

    void Execute(Employee employee);
}
```

Tasks:

```text
TakeOrderTask
CookOrderTask
DeliverOrderTask
CleanTableTask
WashDishTask
RestockTask
```

Employee chooses highest-priority compatible task.

---

# 64. PERFORMANCE TARGETS

Target:

```text
60 FPS @ 1080p
mid-range gaming PC
```

Optimization priorities:

- Pool customers.
- Pool dishes.
- Pool common objects.
- Limit active NPC thinking.
- Stagger AI updates.
- Avoid Update() on hundreds of objects.
- Use LODs.
- Bake lighting where appropriate.

---

# 65. PLAYER EXPERIENCE TARGET

The first 30 minutes should create this emotional progression:

```text
Confusion
→ learning
→ first successful bowl
→ first happy customer
→ first rush
→ controlled panic
→ first profitable day
→ upgrade
→ satisfaction
```

---

# 66. GAMEPLAY STORIES THE SYSTEM SHOULD CREATE

Example:

> The lunch rush begins while the player only has one broth pot. A customer orders three special bowls. The sink is filled with dirty dishes. The refrigerator is almost empty. The player serves one bowl late, receives a bad review, then uses the day's profit to buy a dishwasher.

Another:

> A famous food reviewer visits unexpectedly. The player's best employee calls in sick. The player personally returns to the kitchen and prepares the order.

The game is successful when these stories happen naturally through systems.

---

# 67. WHAT NOT TO DO

Avoid:

- Huge open world.
- Dozens of recipes at launch.
- Overly realistic cooking timers.
- Complex dialogue trees.
- Hundreds of NPCs.
- Multiplayer before single-player works.
- Heavy procedural generation.
- Overcomplicated character customization.
- Trying to simulate every physical detail.

Prioritize the restaurant loop.

---

# 68. PRODUCT ROADMAP

## Prototype

Goal:

```text
Is cooking + serving phở fun?
```

## Vertical Slice

Goal:

```text
Is one full restaurant day fun?
```

## Alpha

Goal:

```text
Can progression sustain several hours?
```

## Beta

Goal:

```text
Is the economy balanced and polished?
```

## Early Access Candidate

Goal:

```text
20–40 hours of progression
```

---

# 69. OPTIONAL FUTURE FEATURES

Post-launch possibilities:

- Workshop/mod support.
- More Vietnamese dishes.
- Additional neighborhoods.
- Seasonal events.
- Restaurant franchises.
- Catering.
- Food truck.
- Co-op multiplayer.
- Challenge mode.
- Speedrun mode.
- Custom recipes.
- Restaurant Steam Workshop assets.

---

# 70. DEVELOPMENT PRIORITY ORDER

Build systems in this exact order:

```text
Player interaction
↓
Food preparation
↓
Orders
↓
Customers
↓
Payment
↓
Day cycle
↓
Economy
↓
Inventory
↓
Progression
↓
Employees
↓
Customization
↓
Additional content
```

Do not build a large world before the restaurant loop is complete.

---

# 71. CLAUDE CODE MASTER PROMPT

Copy everything below into Claude Code after placing this file in the repository.

---

## MASTER PROMPT

You are the lead gameplay engineer and technical architect for an indie game called **Phở Simulator**.

Read this entire `PHO_SIMULATOR_GDD.md` file before making architectural decisions.

The game is a first-person restaurant/life/business simulator where the player starts with a tiny phở shop and manually performs restaurant work before progressively upgrading, hiring staff, automating operations, and expanding the business.

The primary target is PC.

The recommended engine is **Unity 6 using C#**.

Your job is NOT to attempt to build the entire game at once.

You must develop the game incrementally using clean, maintainable, data-driven architecture.

### Primary engineering objectives

1. Produce a stable playable vertical slice before adding breadth.
2. Keep gameplay systems modular.
3. Separate static data/configuration from runtime state.
4. Avoid hard-coded balance values.
5. Keep systems testable.
6. Prefer composition over inheritance.
7. Avoid unnecessary global singleton managers.
8. Use events/interfaces to reduce coupling.
9. Keep code readable enough for a small indie team.
10. Do not add systems that are not required by the current milestone.

### First milestone

Build a playable prototype supporting:

- First-person movement.
- Object interaction.
- Pick up/place objects.
- Ingredient containers.
- Basic inventory.
- One broth pot.
- Noodles.
- Meat.
- Bowl assembly.
- Three phở recipes.
- Restaurant tables.
- Customer spawning.
- Customer seating.
- Customer ordering.
- Order queue.
- Food serving.
- Basic satisfaction.
- Payment.
- Cash balance.
- Day/time system.
- Restaurant opening/closing.
- Save/load.

### Architecture

Create or maintain this structure:

```text
Assets/
  Art/
  Audio/
  Materials/
  Prefabs/
  Scenes/
  Scripts/
    Core/
    Player/
    Interaction/
    Cooking/
    Restaurant/
    Customers/
    Employees/
    Economy/
    Inventory/
    Building/
    UI/
    Save/
    World/
    Events/
  ScriptableObjects/
```

### Core data types

Design ScriptableObjects or equivalent config assets for:

```text
IngredientData
RecipeData
EquipmentData
CustomerArchetype
FurnitureData
GameBalanceConfig
```

Runtime state must not be stored directly in shared ScriptableObjects.

### Interaction architecture

Use an interface similar to:

```csharp
public interface IInteractable
{
    string GetInteractionText();
    void Interact(PlayerInteractor player);
}
```

The player should detect interactions using a center-screen raycast.

### Orders

Order lifecycle:

```text
Created
Waiting
Accepted
Preparing
Ready
Served
Completed
```

Alternative states:

```text
Cancelled
Expired
Refunded
```

Implement order transitions explicitly.

### Customers

Customer state machine:

```text
Spawn
EnterRestaurant
FindTable
Sit
BrowseMenu
Order
WaitForFood
Eat
Pay
Leave
```

Customers must use Unity NavMesh.

Customers should evaluate:

```text
Food quality
Wait time
Correctness
Cleanliness
Price/value
```

Do not implement advanced social AI in the prototype.

### Cooking

The cooking system should simulate:

```text
Broth
Noodles
Meat
Toppings
Assembly
```

The first prototype may simplify broth preparation, but code architecture must support later variables such as:

```text
Cooking time
Ingredient quality
Freshness
Temperature
Recipe accuracy
```

### Inventory

Inventory must support:

```text
Ingredient type
Quantity
Freshness
Storage
Transactions
```

Purchasing and consumption must route through inventory APIs rather than modifying quantities directly.

### Economy

Economy must track:

```text
Cash
Revenue
Ingredient cost
Daily expenses
Profit
```

Do not hard-code item prices into transaction logic.

### Save system

Save:

```text
Player cash
Day/time
Inventory
Restaurant state
Unlocked recipes
Reputation
Progression flags
```

Use versioned save data.

During development JSON is acceptable.

### Coding process

Before implementing each major feature:

1. Inspect the existing repository.
2. Identify the current architecture.
3. Explain which files will be added or modified.
4. Implement the smallest functional version.
5. Compile/check for errors.
6. Add tests for logic-heavy code.
7. Report what changed.
8. Identify the next dependency.

Do not rewrite functioning systems unnecessarily.

### Scope control

If a feature request would substantially increase scope, mark it:

```text
POST-MVP
```

and do not implement it unless explicitly requested.

Examples:

```text
Multiplayer
Multiple cities
Franchise simulation
Advanced relationships
Procedural world generation
Large open world
```

### Development phases

Proceed in this sequence.

#### Phase 1

Player movement + interaction.

#### Phase 2

Physical object pickup/place.

#### Phase 3

Ingredients + inventory.

#### Phase 4

Basic cooking and bowl assembly.

#### Phase 5

Tables + restaurant state.

#### Phase 6

Customers + NavMesh.

#### Phase 7

Order system.

#### Phase 8

Serving + payments.

#### Phase 9

Customer satisfaction.

#### Phase 10

Day/night + finances.

#### Phase 11

Save/load.

#### Phase 12

Upgrade system.

#### Phase 13

Employees.

#### Phase 14

Restaurant customization.

Do not skip ahead unless the required dependency already exists.

### Required engineering style

Prefer:

```text
small classes
explicit state machines
interfaces
event-driven communication
data-driven configuration
dependency injection/reference wiring
object pooling
unit-testable pure functions
```

Avoid:

```text
massive GameManager classes
magic strings
magic numbers
FindObjectOfType calls everywhere
hundreds of Update loops
tight cross-system coupling
```

### Definition of done for the first vertical slice

The project is successful when a player can:

1. Load the game.
2. Walk around the restaurant.
3. Buy or obtain ingredients.
4. Prepare a bowl of phở.
5. Open the restaurant.
6. Receive a customer.
7. Receive an order.
8. Prepare the correct bowl.
9. Serve it.
10. Receive payment.
11. Earn money.
12. Close the restaurant.
13. Save.
14. Reload.
15. Continue the next day.

Until that loop works, prioritize fixing it over adding new content.

### Important

When uncertain about architecture, choose the simplest solution that can reasonably scale to the features described in this GDD.

Do not generate placeholder systems with no gameplay purpose.

Do not create hundreds of scripts at once.

Implement one working subsystem at a time.

---

# 72. CLAUDE CODE — FIRST COMMAND

After Claude Code reads this file, give it this request:

```text
Read PHO_SIMULATOR_GDD.md completely.

We are starting from an empty Unity 6 project.

Act as lead gameplay engineer.

First, design the technical architecture for ONLY the vertical slice.

Do not implement every feature yet.

Create:

1. the proposed folder structure,
2. the core assembly/module boundaries,
3. the core data models,
4. the event architecture,
5. the save-data architecture,
6. the first-person interaction architecture,
7. the order/customer state-machine architecture,
8. a dependency graph showing which systems must be implemented first,
9. a milestone checklist.

Then begin implementing Phase 1:
first-person player movement and the IInteractable raycast interaction system.

After implementation, report exactly:
- files created,
- files modified,
- Unity setup steps required in the editor,
- how to test the system,
- known limitations,
- next recommended task.

Do not start Phase 2 until Phase 1 is internally coherent.
```

---

# 73. CLAUDE CODE — SECOND COMMAND

After Phase 1 works:

```text
Continue Phở Simulator using PHO_SIMULATOR_GDD.md as the master specification.

Implement Phase 2: physical item pickup and placement.

Requirements:

- The player can pick up compatible world objects.
- Held items follow a configurable hold point.
- Player can rotate held items.
- Player can place items on valid surfaces.
- Physics behavior must remain stable.
- The system must work through the existing interaction abstraction.
- Do not hard-code individual object types.
- Add a reusable CarryableObject component.
- Add clear placement validation.
- Keep the system extensible for bowls, ingredient boxes, cleaning tools, and furniture.

After implementation:
- compile/check the project,
- document editor setup,
- document test cases,
- report files changed,
- stop before Phase 3.
```

---

# 74. CLAUDE CODE — THIRD COMMAND

```text
Implement Phase 3 of Phở Simulator: ingredient definitions and restaurant inventory.

Use data-driven ScriptableObjects.

Implement:

- IngredientData
- IngredientCategory
- IngredientStack/runtime inventory entry
- freshness
- quantity
- inventory add/remove APIs
- transaction validation
- debug inventory UI
- test ingredients:
  - noodles
  - beef
  - onion
  - herbs
  - broth base

No supplier system yet.

Inventory logic must be testable without a scene.

Add unit tests for:
- adding inventory
- removing inventory
- insufficient quantity
- freshness updates

Stop after Phase 3.
```

---

# 75. CLAUDE CODE — COOKING PHASE COMMAND

```text
Implement the first playable cooking pipeline.

The player must be able to create a simplified bowl of phở using physical stations.

Prototype recipes:

1. Phở Tái
2. Phở Chín
3. Phở Đặc Biệt

Implement:

- RecipeData
- recipe ingredient requirements
- bowl object
- noodle station
- meat station
- broth pot
- topping station
- bowl assembly state
- recipe matching
- food quality score
- completed dish object

For the prototype, use simplified interactions rather than animations.

A dish must record exactly what ingredients were placed in it.

Recipe matching must compare the actual dish contents against RecipeData.

Do not fake recipe completion by clicking a single "cook" button.

The player should physically perform the bowl assembly sequence.

Add tests for recipe matching and quality calculation.
```

---

# 76. CLAUDE CODE — CUSTOMER PHASE COMMAND

```text
Implement the first restaurant customer loop.

Requirements:

- CustomerSpawner
- CustomerArchetype ScriptableObject
- Customer state machine
- NavMesh movement
- restaurant entrance
- table registry
- seat availability
- customer seating
- menu order generation
- RestaurantOrder model
- OrderManager
- customer wait timer
- customer receives served dish
- dish correctness evaluation
- eating timer
- payment
- customer exits

Start with one customer at a time.

Once one customer works reliably, support configurable concurrent customers.

Expose debug state text over each customer while developing.

Do not implement complex animation.
```

---

# 77. CLAUDE CODE — ECONOMY PHASE COMMAND

```text
Implement the core Phở Simulator economy.

Requirements:

- player cash balance
- recipe selling prices
- ingredient cost tracking
- sale transaction
- daily revenue
- daily ingredient cost
- daily profit
- restaurant opening
- restaurant closing
- daily summary UI

Add a configurable GameBalanceConfig.

No economy constants should be hidden in gameplay code.

Add tests covering:
- successful payment
- price calculation
- daily profit calculation
- invalid negative transaction prevention
```

---

# 78. CLAUDE CODE — SAVE PHASE COMMAND

```text
Implement a versioned save/load system for the Phở Simulator vertical slice.

Persist:

- cash
- current day
- current time
- inventory
- unlocked recipes
- reputation
- restaurant open/closed state
- progression flags

Use JSON during development.

Requirements:

- save schema version
- safe missing-field handling
- corrupted-save fallback
- manual Save button
- Load button
- autosave at end of day

Do not serialize MonoBehaviours or ScriptableObjects directly.

Serialize stable IDs and runtime values.
```

---

# 79. CLAUDE CODE — EMPLOYEE PHASE COMMAND

```text
Begin the employee prototype.

Implement only one employee role first: waiter.

Use a reusable task-based employee architecture.

Waiter tasks:

1. deliver ready food
2. take orders
3. clear finished tables

Implement:

- Employee
- EmployeeDefinition
- EmployeeStats
- IRestaurantTask
- task registry/dispatcher
- priority evaluation
- waiter navigation
- task claim/release behavior

The system must prevent two employees from unintentionally claiming the same exclusive task.

Do not implement advanced schedules or morale yet.
```

---

# 80. CLAUDE CODE — BUILD MODE COMMAND

```text
Implement a basic restaurant build mode.

Requirements:

- enter/exit build mode
- furniture catalog
- furniture preview
- grid snapping
- rotate object
- placement validation
- purchase cost
- remove/sell object
- persisted furniture layout

Initial objects:

- small table
- chair
- trash bin
- prep table
- shelf

Do not build advanced architectural editing.

Walls, doors, and building structure remain fixed during MVP.
```

---

# 81. MVP ACCEPTANCE CHECKLIST

Before expanding scope, verify all of the following.

## Interaction

- [ ] Player movement feels responsive.
- [ ] Interaction prompts are clear.
- [ ] Carryable objects behave correctly.

## Cooking

- [ ] Broth station works.
- [ ] Noodle station works.
- [ ] Meat station works.
- [ ] Toppings work.
- [ ] Bowls preserve their ingredient state.
- [ ] Recipes are data-driven.
- [ ] Incorrect bowls are possible.

## Customers

- [ ] Customers enter.
- [ ] Customers find seats.
- [ ] Customers order.
- [ ] Customers wait.
- [ ] Customers receive food.
- [ ] Customers pay.
- [ ] Customers leave.

## Economy

- [ ] Player earns money.
- [ ] Ingredients have costs.
- [ ] Prices are editable.
- [ ] Daily profit is calculated.

## Progression

- [ ] At least one meaningful upgrade exists.
- [ ] Upgrade visibly improves workflow.

## Persistence

- [ ] Save works.
- [ ] Load works.
- [ ] Inventory persists.
- [ ] Money persists.
- [ ] Day persists.

---

# 82. FINAL PRODUCT VISION

The strongest version of Phở Simulator should create a satisfying contrast between its beginning and end.

At the beginning:

```text
The player wakes early.
Drives or walks to buy meat.
Carries boxes into a tiny restaurant.
Cooks broth.
Washes bowls.
Serves every customer personally.
Counts every dollar.
```

Later:

```text
Suppliers deliver ingredients.
Prep staff arrive before opening.
Waiters handle customers.
Cooks operate multiple stations.
Delivery orders constantly arrive.
The restaurant has a recognizable brand.
The player manages expansion while occasionally jumping back into the kitchen during a rush.
```

The key fantasy is not simply cooking phở.

It is:

> **building a tiny phở business into something the player feels personally responsible for creating.**

That progression should guide every design decision.
