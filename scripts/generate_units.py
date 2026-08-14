#!/usr/bin/env python3
"""Generates Assets/StreamingAssets/Data/units.json — the unit catalogue.

The same catalogue is used for both teams (User/Blue and Enemy/Red); a team is
assigned when a unit is placed on the map. All values are company-equivalent;
echelon multipliers in Enums.cs scale them at runtime.

Two orthogonal classifications are written for every type:

  category  — how the unit BEHAVES. "CoreGround" holds terrain and gets a ground
              model; "Drone", "Air" and "Naval" do neither. This is what the
              gameplay code branches on (UnitModelLibrary, CombatSystem).
  branch    — which ARM the unit belongs to. Purely a taxonomy for the player:
              Infantry, Armour, Mechanised, Artillery, AntiAircraft, Air, Navy,
              Logistics, Other. Shown and filtered on the Units screen.

Keeping them apart is the point: a MANPADS team and a fighter squadron are both
air-defence-adjacent but behave nothing alike, and an attack helicopter is an
Air *branch* unit that must not be handed an infantryman for a model.

Run:  python scripts/generate_units.py
"""
import json, os

U = []

def unit(id, name, cat, br, atk, hard, dfn, arm, aa, wrng, vrng, mp, trn, mor,
         org, spd, ammoType, ammoStock, fuel, fuelKm, food, supply,
         indirect=False, cuas=False, support=False, desc=""):
    U.append(dict(id=id, name=name, category=cat, branch=br, description=desc,
                  attack=atk, hardAttack=hard, defence=dfn, armour=arm,
                  antiAir=aa, weaponRangeKm=wrng, viewRangeKm=vrng,
                  manpower=mp, training=trn, morale=mor, organisation=org,
                  speedKmh=spd, ammoType=ammoType, ammoStock=ammoStock,
                  fuelStock=fuel, fuelUsePerKm=fuelKm, foodDays=food,
                  supplyUsePerDay=supply, canIndirectFire=indirect,
                  canCounterUas=cuas, isSupport=support))

# ============================ INFANTRY ============================
# Dismounted manoeuvre formations. They fight on their feet, so fuelUsePerKm is
# 0 and speed is walking pace unless the type has organic transport.
unit("infantry",            "Infantry",              "CoreGround", "Infantry", 30, 5, 40, 5, 8, 0.8, 2.0, 120, 60, 65, 60, 5,  "5.56mm NATO",      42000, 0,     0.0, 3, 1.0, desc="Foot infantry. Strong in rough and urban terrain.")
unit("light_infantry",      "Light Infantry",        "CoreGround", "Infantry", 32, 6, 38, 4, 8, 0.9, 2.4, 110, 68, 70, 64, 7,  "5.56mm NATO",      38000, 0,     0.0, 3, 0.9, desc="Stripped-down infantry optimised for difficult terrain and rapid movement.")
unit("airborne_infantry",   "Airborne Infantry",     "CoreGround", "Infantry", 38, 8, 40, 5, 9, 0.9, 2.4, 115, 78, 80, 68, 6,  "5.56mm NATO",      36000, 0,     0.0, 3, 1.0, desc="Parachute infantry. Inserts deep, then fights light and hard.")
unit("air_assault_infantry", "Air Assault Infantry", "CoreGround", "Infantry", 36, 10, 38, 6, 10, 1.0, 2.6, 115, 76, 76, 66, 6, "5.56mm NATO",      38000, 0,     0.0, 3, 1.0, desc="Helicopter-borne manoeuvre infantry. Seizes objectives behind the line.")
unit("mountain_infantry",   "Mountain Infantry",     "CoreGround", "Infantry", 33, 6, 44, 5, 8, 0.9, 2.6, 105, 74, 72, 64, 5,  "5.56mm NATO",      36000, 0,     0.0, 4, 0.9, desc="Alpine and high-ground specialists. Holds terrain nothing else can reach.")
unit("marine_infantry",     "Marine Infantry",       "CoreGround", "Infantry", 40, 14, 42, 12, 9, 1.0, 2.4, 125, 74, 74, 66, 20, "5.56mm + 40mm",   44000, 4200,  2.0, 4, 1.2, desc="Amphibious infantry. Fights from the water's edge inland.")
unit("special_forces",      "Special Forces",        "CoreGround", "Infantry", 55, 15, 45, 5, 10, 1.0, 4.5, 40, 95, 90, 80, 8,  "5.56mm/7.62mm",    16000, 0,     0.0, 5, 0.7, desc="Elite raiding, DA and deep reconnaissance element.")

# ============================ ARMOUR ============================
# Tracked and armoured fighting formations, plus the anti-armour arm that lives
# and dies against them.
unit("armour",              "Armour",                "CoreGround", "Armour", 60, 85, 55, 90, 10, 3.0, 2.5, 60, 70, 70, 65, 45, "120mm APFSDS/HE",  1600,  22000, 8.5, 3, 2.0, desc="Main battle tanks. Breakthrough and shock element.")
unit("combined_arms",       "Combined Arms",         "CoreGround", "Armour", 62, 60, 58, 60, 14, 2.5, 3.0, 150, 72, 72, 68, 38, "Mixed 120mm/25mm/5.56mm", 40000, 16000, 6.0, 3, 2.4, desc="Integrated armour, infantry and supporting fires under one commander.")
unit("armoured_recon",      "Armoured Reconnaissance", "CoreGround", "Armour", 30, 25, 30, 40, 9, 2.0, 5.5, 60, 76, 72, 70, 55, "30mm + ATGM",     9000,  5600,  3.2, 3, 1.2, desc="Cavalry. Screens the force and fights for information in vehicles.")
unit("recon",               "Reconnaissance",        "CoreGround", "Armour", 20, 12, 25, 20, 8, 1.2, 6.0, 45, 75, 70, 70, 65, "7.62mm + 30mm",    18000, 4200,  2.4, 4, 0.8, desc="Screens, scouts and finds the enemy. Very high view range.")
unit("anti_tank",           "Anti-tank",             "CoreGround", "Armour", 25, 90, 45, 10, 2, 4.0, 2.5, 55, 68, 64, 60, 25, "ATGM (Javelin-class)", 96, 1800, 1.6, 3, 1.0, desc="ATGM teams and tank destroyers. Lethal vs armour.")

# ============================ MECHANISED ============================
# Infantry that rides to the fight. Armour and speed come from the vehicle, so
# these carry the fuel bill the foot branch does not.
unit("mech_infantry",       "Mechanised Infantry",   "CoreGround", "Mechanised", 45, 25, 50, 35, 12, 1.5, 2.5, 130, 65, 68, 62, 35, "5.56mm + 25mm", 52000, 9000,  4.2, 3, 1.4, desc="Infantry mounted in IFVs, fights mounted or dismounted.")
unit("mot_infantry",        "Motorised Infantry",    "CoreGround", "Mechanised", 35, 8, 42, 10, 9, 1.0, 2.2, 125, 62, 66, 61, 55, "5.56mm NATO",   46000, 5200,  2.0, 3, 1.2, desc="Truck/APC-mobile infantry. Fast on roads, light protection.")

# ============================ ARTILLERY ============================
# The fires branch: everything that shoots at something it cannot see, plus the
# observers and sensors that tell it where to shoot.
unit("artillery",           "Artillery",             "CoreGround", "Artillery", 75, 30, 15, 8, 2, 24.0, 1.5, 90, 65, 60, 55, 35, "155mm HE/SMART",   960,   6800,  3.0, 3, 2.2, indirect=True, desc="Tube artillery battalion slice. Long-range fires.")
unit("self_propelled_artillery", "Self-Propelled Artillery", "CoreGround", "Artillery", 80, 34, 22, 25, 2, 30.0, 1.8, 85, 68, 62, 58, 55, "155mm HE/SMART", 800, 9500, 4.5, 3, 2.3, indirect=True, desc="Armoured guns that shoot and scoot. Survives counter-battery.")
unit("towed_artillery",     "Towed Artillery",       "CoreGround", "Artillery", 68, 26, 14, 4, 1, 20.0, 1.4, 95, 62, 58, 54, 18, "155mm HE",         1100,  2400,  1.6, 3, 2.0, indirect=True, desc="Light towed guns. Cheap and air-portable, slow to displace.")
unit("rocket_artillery",    "Rocket Artillery",      "CoreGround", "Artillery", 90, 35, 12, 8, 2, 70.0, 1.5, 80, 68, 62, 55, 40, "227mm GMLRS",      144,   7600,  3.4, 3, 2.6, indirect=True, desc="MLRS. Very long-range saturation and precision fires.")
unit("mortar",              "Mortar",                "CoreGround", "Artillery", 45, 10, 18, 5, 1, 7.0, 1.2, 60, 60, 60, 58, 12, "120mm mortar",     720,   900,   1.0, 3, 1.2, indirect=True, desc="Close-support indirect fires organic to infantry.")
unit("surface_to_surface_missile", "Surface-to-Surface Missile", "CoreGround", "Artillery", 110, 55, 10, 8, 1, 300.0, 1.5, 70, 72, 62, 56, 38, "SRBM (ATACMS-class)", 24, 8200, 3.6, 3, 2.8, indirect=True, desc="Theatre missile battery. Reaches the enemy's depth in one shot.")
unit("deep_precision_strike", "Deep Precision Strike", "CoreGround", "Artillery", 100, 70, 10, 8, 1, 150.0, 1.5, 65, 78, 64, 58, 40, "Precision cruise missile", 32, 7800, 3.4, 3, 2.6, indirect=True, desc="Ground-launched precision fires against high-value targets.")
unit("coastal_defence_missile", "Coastal Defence Missile", "CoreGround", "Artillery", 95, 80, 12, 8, 2, 200.0, 2.0, 60, 74, 62, 56, 35, "Anti-ship missile", 24, 6400, 3.0, 3, 2.2, indirect=True, desc="Land-based anti-ship battery. Closes a sea lane from the shore.")
unit("forward_observer",    "Forward Observer",      "CoreGround", "Artillery", 10, 4, 20, 5, 3, 1.0, 8.0, 10, 82, 70, 62, 8,  "5.56mm NATO",      3000,  0,     0.0, 4, 0.5, support=True, desc="Eyes for the guns. Sharply improves artillery accuracy.")
unit("joint_fires",         "Joint Fires Cell",      "CoreGround", "Artillery", 5, 2, 16, 6, 2, 0.5, 5.0, 20, 85, 66, 62, 30, "5.56mm NATO",      2500,  1800,  1.4, 3, 1.1, support=True, desc="Coordinates artillery, UAS and air. Fires multiplier.")
unit("jtac",                "JTAC / Air Control Party", "CoreGround", "Artillery", 10, 4, 18, 5, 3, 1.0, 9.0, 12, 88, 72, 64, 30, "5.56mm NATO",   2400,  1200,  1.2, 4, 0.6, support=True, desc="Talks close air support onto the target. The link between ground and air.")
unit("target_acquisition",  "Target-acquisition Unit", "CoreGround", "Artillery", 3, 1, 15, 6, 4, 30.0, 30.0, 30, 78, 62, 58, 30, "5.56mm NATO",    3500,  2400,  1.6, 3, 1.2, support=True, desc="Acoustic and optical sensors that locate enemy firing positions.")
unit("counter_battery_radar", "Counter-Battery Radar", "CoreGround", "Artillery", 2, 1, 14, 6, 5, 45.0, 45.0, 28, 80, 62, 58, 32, "5.56mm NATO",    3000,  2600,  1.8, 3, 1.3, support=True, desc="Tracks incoming shells back to the gun that fired them.")

# ============================ ANTI-AIRCRAFT ============================
# Ground-based air and missile defence, and the radars and C2 that make it a
# system rather than a collection of launchers.
unit("manpads",             "MANPADS",               "CoreGround", "AntiAircraft", 4, 2, 22, 4, 55, 6.0, 5.0, 30, 68, 64, 58, 6, "MANPADS (Stinger-class)", 60, 0, 0.0, 3, 0.7, cuas=True, desc="Shoulder-launched SAM teams. Very short range, goes anywhere infantry goes.")
unit("air_defence",         "Air Defence",           "CoreGround", "AntiAircraft", 10, 5, 30, 15, 80, 6.0, 5.0, 65, 66, 62, 58, 30, "35mm AHEAD",     4200,  3600,  2.2, 3, 1.4, cuas=True, desc="Gun/short-range AD. Protects manoeuvre units from air and UAS.")
unit("shorad",              "SHORAD",                "CoreGround", "AntiAircraft", 8, 6, 28, 20, 75, 12.0, 8.0, 55, 70, 64, 58, 45, "SHORAD missile/gun", 220, 4200, 2.4, 3, 1.5, cuas=True, desc="Mobile short-range air defence that keeps up with the manoeuvre force.")
unit("spaag",               "Self-Propelled AA Gun", "CoreGround", "AntiAircraft", 25, 12, 30, 30, 78, 4.0, 4.0, 50, 68, 64, 58, 45, "30mm AA",        6000,  5200,  3.0, 3, 1.6, cuas=True, desc="Tracked AA guns. Murderous against helicopters, drones and light troops.")
unit("mrad",                "Medium-Range Air Defence", "CoreGround", "AntiAircraft", 4, 2, 24, 12, 92, 50.0, 10.0, 75, 72, 64, 56, 30, "MRAD missile (IRIS-T SLM-class)", 48, 4800, 2.8, 3, 1.9, cuas=True, desc="Area air defence over a division's ground.")
unit("sam",                 "Surface-to-air Missile", "CoreGround", "AntiAircraft", 5, 2, 25, 12, 95, 40.0, 8.0, 70, 70, 62, 55, 30, "SAM (NASAMS-class)", 48, 4200, 2.6, 3, 1.8, cuas=True, desc="Medium/long-range SAM battery. Area air denial.")
unit("lrad",                "Long-Range Air Defence", "CoreGround", "AntiAircraft", 3, 1, 22, 12, 98, 120.0, 12.0, 90, 76, 64, 56, 25, "LRAD missile (Patriot-class)", 32, 6200, 3.4, 3, 2.4, cuas=True, desc="Strategic air defence. Denies an entire operational sector to aircraft.")
unit("missile_defence",     "Missile Defence",       "CoreGround", "AntiAircraft", 2, 1, 20, 12, 96, 150.0, 14.0, 85, 80, 66, 58, 24, "Interceptor missile", 24, 6000, 3.2, 3, 2.5, cuas=True, desc="Ballistic and cruise missile interception.")
unit("counter_uas",         "Counter-UAS",           "CoreGround", "AntiAircraft", 6, 2, 22, 10, 70, 8.0, 6.0, 35, 72, 62, 58, 30, "C-UAS effectors",  600,   2400,  1.8, 3, 1.2, cuas=True, desc="Dedicated drone-defeat: jammers, guns, interceptors.")
unit("ad_radar",            "Air-defence Radar",     "CoreGround", "AntiAircraft", 1, 0, 14, 8, 25, 60.0, 60.0, 30, 70, 60, 55, 28, "5.56mm NATO",     3000,  2600,  1.8, 3, 1.4, cuas=True, support=True, desc="Surveillance radar. Extends air picture for AD and C-UAS.")
unit("air_surveillance_radar", "Air Surveillance Radar", "CoreGround", "AntiAircraft", 1, 0, 14, 8, 20, 250.0, 250.0, 35, 76, 62, 56, 26, "5.56mm NATO", 2500, 3000, 2.0, 3, 1.6, cuas=True, support=True, desc="Long-range radar generating the regional air picture.")
unit("air_defence_c2",      "Air Defence C2",        "CoreGround", "AntiAircraft", 3, 1, 18, 8, 30, 1.0, 8.0, 40, 82, 68, 70, 32, "5.56mm NATO",     3000,  2800,  1.8, 4, 1.6, cuas=True, support=True, desc="Ties sensors to shooters. Without it the AD units fight alone.")

# ============================ AIR ============================
# Manned aviation and unmanned systems. Category is "Air" for crewed airframes
# and "Drone" for unmanned ones — neither holds ground, and neither is handed
# the stand-in infantryman model.
unit("attack_helicopter",   "Attack Helicopter",     "Air", "Air", 70, 85, 20, 18, 25, 8.0, 12.0, 40, 84, 72, 66, 260, "ATGM + 30mm",    900,   4500,  6.0, 3, 2.6, desc="Rotary-wing anti-armour. Arrives fast, kills tanks, leaves.")
unit("recon_helicopter",    "Reconnaissance Helicopter", "Air", "Air", 20, 15, 14, 10, 10, 4.0, 20.0, 25, 80, 68, 64, 270, "70mm + 12.7mm", 600, 3600, 5.0, 3, 1.6, desc="Armed scout helicopters. Finds targets for the attack flight.")
unit("transport_helicopter", "Transport Helicopter", "Air", "Air", 4, 2, 14, 12, 6, 0.5, 10.0, 45, 76, 68, 62, 250, "7.62mm door guns", 4000, 5200, 6.5, 3, 2.2, support=True, desc="Lifts an air assault company over the front line.")
unit("utility_helicopter",  "Utility Helicopter",    "Air", "Air", 10, 4, 16, 12, 8, 1.0, 10.0, 35, 74, 66, 62, 250, "7.62mm door guns", 5000, 4200, 5.5, 3, 1.8, support=True, desc="General-purpose battlefield aviation: liaison, resupply, lift.")
unit("medevac_helicopter",  "MEDEVAC Helicopter",    "Air", "Air", 1, 0, 12, 10, 4, 0.2, 10.0, 25, 78, 68, 62, 250, "None (protected)", 0, 4000, 5.2, 3, 1.7, support=True, desc="Air casualty evacuation. Turns wounded into returned soldiers.")
unit("cas_aircraft",        "Close Air Support",     "Air", "Air", 85, 90, 18, 20, 30, 12.0, 30.0, 40, 86, 72, 66, 700, "30mm + guided bombs", 1400, 11000, 14.0, 3, 3.0, desc="Ground-attack aircraft working directly for the ground commander.")
unit("strike_aircraft",     "Strike Aircraft",       "Air", "Air", 95, 85, 16, 15, 40, 60.0, 40.0, 38, 86, 72, 66, 1400, "Guided bombs/cruise", 60, 13000, 20.0, 3, 3.2, desc="Operational-depth strike. Bridges, depots, headquarters.")
unit("fighter_aircraft",    "Fighter",               "Air", "Air", 30, 20, 20, 15, 98, 90.0, 120.0, 35, 88, 74, 68, 1900, "AAM + 20mm",   700,   14000, 22.0, 3, 3.2, cuas=True, desc="Air superiority. Everything else in the air depends on this.")
unit("isr_aircraft",        "ISR Aircraft",          "Air", "Air", 2, 1, 12, 8, 4, 1.0, 300.0, 45, 88, 66, 62, 600, "None (protected)", 0, 30000, 14.0, 4, 2.6, support=True, desc="Airborne surveillance. Sees deeper than anything on the ground.")
unit("aewc",                "Airborne Early Warning", "Air", "Air", 2, 1, 12, 8, 15, 1.0, 400.0, 55, 90, 68, 66, 750, "None (protected)", 0, 45000, 16.0, 4, 3.0, cuas=True, support=True, desc="Airborne radar and battle management. The air picture, airborne.")
unit("transport_aircraft",  "Tactical Airlift",      "Air", "Air", 2, 1, 12, 10, 4, 0.5, 40.0, 60, 76, 66, 62, 700, "None (protected)", 0, 40000, 18.0, 4, 2.8, support=True, desc="Moves troops and freight between theatres and onto rough strips.")
unit("aerial_refuelling",   "Air-to-Air Refuelling", "Air", "Air", 1, 0, 10, 8, 2, 0.5, 60.0, 40, 80, 64, 62, 800, "None (protected)", 0, 90000, 20.0, 4, 2.8, support=True, desc="Extends every other airframe's reach and time on station.")
unit("ew_aircraft",         "Airborne Electronic Warfare", "Air", "Air", 4, 2, 12, 8, 20, 200.0, 200.0, 40, 88, 68, 64, 850, "Anti-radiation missile", 12, 20000, 16.0, 3, 2.8, cuas=True, support=True, desc="Jams and kills radars so the strike package gets through.")

unit("uas_operator",        "UAS Operator Team",     "CoreGround", "Air", 8, 3, 15, 4, 4, 12.0, 12.0, 12, 78, 66, 60, 8, "sUAS batteries",  60,    0,     0.0, 3, 0.6, support=True, desc="Small-UAS crews flying quadcopters and fixed-wing sUAS.")
unit("recon_uas",           "Reconnaissance UAS",    "Drone", "Air", 4, 2, 10, 3, 2, 50.0, 50.0, 25, 76, 64, 58, 15, "UAS airframes",    18,    1200,  1.0, 3, 1.0, support=True, desc="Tactical ISR drones. Deep, persistent surveillance.")
unit("armed_uas",           "Armed UAS",             "Drone", "Air", 45, 40, 12, 4, 3, 60.0, 45.0, 30, 80, 66, 58, 15, "Guided munitions", 36,    1600,  1.2, 3, 1.4, indirect=True, desc="MALE/attack drones with guided munitions.")
unit("loitering_munition",  "Loitering-munition Unit", "Drone", "Air", 55, 60, 10, 3, 2, 40.0, 25.0, 22, 74, 64, 56, 15, "Loitering munitions", 48, 900, 0.8, 3, 1.3, indirect=True, desc="One-way attack drones. High lethality vs vehicles and guns.")
unit("fpv_attack_uas",      "FPV Attack UAS",        "Drone", "Air", 35, 45, 6, 2, 2, 15.0, 12.0, 14, 70, 66, 54, 120, "FPV munitions",   120,   0,     0.0, 3, 0.9, indirect=True, desc="Cheap first-person-view one-way drones. Attrition weapon of the modern front.")
unit("deep_strike_uas",     "Deep-Strike UAS",       "Drone", "Air", 85, 55, 8, 3, 2, 800.0, 30.0, 26, 78, 64, 56, 180, "One-way attack airframes", 18, 2600, 0.6, 3, 1.8, indirect=True, desc="Long-range one-way attack drones. Reaches the strategic rear.")
unit("interceptor_uas",     "Interceptor UAS",       "Drone", "Air", 5, 2, 8, 2, 82, 20.0, 20.0, 18, 78, 64, 56, 200, "Interceptor drones", 90,  0,     0.0, 3, 1.0, cuas=True, desc="Drone-on-drone interception. The cheap answer to cheap drones.")
unit("ew_uas",              "Electronic Warfare UAS", "Drone", "Air", 3, 1, 8, 3, 12, 60.0, 30.0, 16, 80, 64, 58, 140, "None (protected)", 0,   1100,  0.9, 3, 1.2, cuas=True, support=True, desc="Airborne jamming from a small airframe. Denies links and GNSS forward.")
unit("relay_uas",           "Communications Relay UAS", "Drone", "Air", 1, 0, 8, 3, 1, 0.2, 60.0, 12, 74, 62, 58, 100, "None (protected)", 0, 900, 0.8, 3, 0.9, support=True, desc="Airborne relay extending the tactical network past terrain masking.")
unit("cargo_uas",           "Cargo UAS",             "Drone", "Air", 1, 0, 8, 3, 1, 0.2, 20.0, 14, 70, 62, 56, 90, "None (protected)",  0,     800,   0.8, 3, 0.9, support=True, desc="Unmanned resupply to positions a truck cannot reach.")
unit("decoy_uas",           "Decoy UAS",             "Drone", "Air", 1, 0, 6, 2, 1, 0.2, 10.0, 10, 68, 60, 54, 250, "Decoy airframes", 40,    600,   0.6, 3, 0.8, support=True, desc="Saturates air defences and makes them show themselves.")

# ============================ NAVY ============================
# Vessels. Enormous fuel and ration figures because a ship carries weeks of both
# — the sustainment model reads them the same way it reads a truck's.
unit("surface_combatant",   "Surface Combatant",     "Naval", "Navy", 70, 60, 55, 45, 85, 120.0, 150.0, 220, 82, 70, 68, 55, "VLS missiles + 76mm", 120, 900000, 60.0, 30, 4.0, indirect=True, cuas=True, desc="Frigate/destroyer. Area air defence, land attack and sea control.")
unit("submarine",           "Submarine",             "Naval", "Navy", 80, 70, 40, 30, 1, 60.0, 20.0, 60, 88, 72, 70, 37, "Heavyweight torpedoes", 24, 400000, 30.0, 60, 2.4, indirect=True, desc="Undersea warfare. Cannot be found, cannot defend itself once it is.")
unit("patrol_craft",        "Patrol Craft",          "Naval", "Navy", 22, 12, 25, 15, 20, 8.0, 25.0, 30, 70, 64, 60, 65, "30mm + 12.7mm",  4000,  40000, 22.0, 7, 1.4, desc="Fast littoral and coastal patrol. Cheap presence in narrow water.")
unit("mine_countermeasure_ship", "Mine Countermeasure Vessel", "Naval", "Navy", 6, 2, 22, 12, 8, 1.0, 15.0, 45, 76, 62, 58, 28, "Mine disposal charges", 60, 60000, 18.0, 14, 1.6, support=True, desc="Clears naval mines. Slow, fragile, and the only way a port reopens.")
unit("amphibious_ship",     "Amphibious Warfare Ship", "Naval", "Navy", 8, 4, 35, 30, 25, 2.0, 40.0, 180, 74, 66, 64, 40, "Self-defence guns", 3000, 700000, 55.0, 30, 3.6, support=True, desc="Lands marine infantry and its vehicles across a beach.")
unit("naval_logistics",     "Naval Logistics",       "Naval", "Navy", 3, 1, 20, 15, 6, 0.5, 25.0, 120, 66, 60, 58, 33, "Self-defence guns", 1500, 900000, 50.0, 45, 3.8, support=True, desc="Replenishment at sea. Keeps a fleet on station instead of in port.")

# ============================ LOGISTICS ============================
# Sustainment. High foodDays and fuelStock, negligible combat value: these are
# what the rest of the order of battle is actually fighting to protect.
unit("logistics",           "Logistics",             "CoreGround", "Logistics", 3, 1, 12, 5, 1, 0.3, 1.0, 95, 55, 58, 55, 45, "5.56mm NATO",   8000,  16000, 2.8, 6, 2.4, support=True, desc="Sustainment planning and distribution node.")
unit("supply",              "Supply",                "CoreGround", "Logistics", 2, 1, 10, 4, 1, 0.3, 1.0, 80, 52, 56, 54, 45, "5.56mm NATO",   6000,  14000, 2.6, 8, 2.2, support=True, desc="Ammunition, fuel and rations resupply point.")
unit("ammunition",          "Ammunition Supply",     "CoreGround", "Logistics", 2, 1, 12, 5, 1, 0.3, 1.0, 85, 55, 56, 55, 42, "Mixed natures", 60000, 9000,  2.6, 6, 2.4, support=True, desc="Dedicated ammunition holding and distribution. Guns stop without it.")
unit("fuel_pol",            "Fuel / POL",            "CoreGround", "Logistics", 2, 1, 10, 4, 1, 0.3, 1.0, 75, 55, 56, 54, 40, "5.56mm NATO",   4000,  60000, 3.2, 6, 2.6, support=True, desc="Bulk fuel storage and distribution. The armoured force's real limit.")
unit("transport",           "Transport",             "CoreGround", "Logistics", 2, 1, 10, 4, 1, 0.3, 1.0, 70, 52, 56, 54, 60, "5.56mm NATO",   5000,  20000, 3.0, 5, 2.0, support=True, desc="Truck transport. Moves units and freight.")
unit("movement_control",    "Movement Control",      "CoreGround", "Logistics", 4, 1, 14, 5, 1, 0.4, 2.0, 35, 62, 58, 60, 55, "9mm/5.56mm",    4000,  2800,  1.6, 4, 1.1, support=True, desc="Runs the route network so convoys do not meet each other head-on.")
unit("maintenance",         "Maintenance",           "CoreGround", "Logistics", 2, 1, 12, 5, 1, 0.3, 1.0, 65, 60, 58, 56, 40, "5.56mm NATO",   4500,  5200,  2.2, 4, 1.8, support=True, desc="Repair and recovery of vehicles and equipment.")
unit("recovery",            "Recovery",              "CoreGround", "Logistics", 2, 1, 14, 8, 1, 0.3, 1.0, 55, 62, 58, 56, 35, "5.56mm NATO",   3500,  9000,  4.0, 4, 1.8, support=True, desc="Drags casualties of the mechanical kind off the battlefield.")
unit("ordnance",            "Ordnance",              "CoreGround", "Logistics", 3, 2, 14, 6, 1, 0.3, 1.0, 60, 66, 58, 58, 38, "5.56mm NATO",   5000,  4600,  2.0, 4, 1.6, support=True, desc="Technical support to weapons and ammunition.")
unit("medical",             "Medical",               "CoreGround", "Logistics", 1, 0, 12, 5, 1, 0.2, 1.0, 70, 64, 62, 58, 40, "None (protected)", 0,  4200,  2.0, 5, 1.6, support=True, desc="Role 1/2 medical. Reduces losses of nearby units.")
unit("medevac",             "Medical Evacuation",    "CoreGround", "Logistics", 1, 0, 10, 6, 1, 0.2, 1.5, 45, 70, 64, 60, 55, "None (protected)", 0,  5200,  2.4, 4, 1.4, support=True, desc="Ground casualty evacuation to the treatment chain.")
unit("field_hospital",      "Field Hospital",        "CoreGround", "Logistics", 1, 0, 14, 5, 1, 0.2, 1.0, 150, 72, 62, 58, 12, "None (protected)", 0, 6000,  2.2, 7, 2.6, support=True, desc="Role 2/3 surgical capability. Static, large, and worth defending.")
unit("water_supply",        "Water Supply",          "CoreGround", "Logistics", 1, 0, 10, 4, 1, 0.2, 1.0, 50, 52, 56, 54, 40, "5.56mm NATO",   2500,  8000,  2.4, 8, 1.8, support=True, desc="Purification and distribution. The first thing to run out in the heat.")
unit("field_services",      "Field Services",        "CoreGround", "Logistics", 1, 0, 10, 4, 1, 0.2, 1.0, 60, 50, 56, 54, 38, "5.56mm NATO",   2500,  5000,  2.0, 8, 1.9, support=True, desc="Laundry, bath, mortuary affairs and the rest of what keeps troops human.")
unit("personnel_support",   "Personnel Support",     "CoreGround", "Logistics", 1, 0, 10, 4, 1, 0.2, 1.0, 40, 55, 58, 58, 40, "9mm",           1500,  2200,  1.4, 5, 1.2, support=True, desc="Replacements and administration. Refills formations that have bled.")

# ============================ OTHER ============================
# Combat support that belongs to no single arm: engineers, ISR, C2, information
# and cyber. All support=True — they fight badly and are meant to.
unit("headquarters",        "Headquarters",          "CoreGround", "Other", 5, 2, 20, 10, 2, 0.4, 3.0, 75, 72, 68, 70, 35, "5.56mm NATO",   9000,  3600,  2.0, 4, 2.0, support=True, desc="Command element. Boosts organisation of nearby units.")
unit("tactical_cp",         "Tactical Command Post", "CoreGround", "Other", 4, 2, 18, 8, 2, 0.5, 4.0, 35, 76, 66, 68, 30, "5.56mm NATO",   4000,  2600,  1.8, 4, 1.6, support=True, desc="Forward C2 node for the drone/fires fight.")
unit("signals",             "Signals",               "CoreGround", "Other", 5, 2, 18, 6, 1, 0.3, 1.2, 50, 66, 60, 58, 40, "5.56mm NATO",   8000,  2400,  1.6, 3, 1.3, support=True, desc="Communications backbone. Enables command and control.")
unit("engineer",            "Engineer",              "CoreGround", "Other", 25, 15, 40, 15, 3, 0.8, 1.8, 85, 62, 63, 60, 30, "5.56mm + demolitions", 24000, 4800, 2.4, 3, 1.6, support=True, desc="Mobility, counter-mobility, fortifications and breaching.")
unit("assault_engineer",    "Assault Engineer",      "CoreGround", "Other", 35, 25, 42, 30, 3, 0.8, 1.8, 80, 70, 68, 62, 32, "5.56mm + demolitions", 20000, 6200, 3.2, 3, 1.7, support=True, desc="Pioneers who breach obstacles under fire, with the assault.")
unit("bridging",            "Bridging",              "CoreGround", "Other", 3, 1, 16, 10, 1, 0.3, 1.0, 70, 60, 58, 56, 25, "5.56mm NATO",   3000,  12000, 5.0, 4, 2.0, support=True, desc="Tactical bridging. Turns a river from a stop line into a crossing.")
unit("route_clearance",     "Route Clearance",       "CoreGround", "Other", 12, 8, 26, 22, 2, 0.6, 1.6, 60, 72, 64, 60, 28, "5.56mm + demolitions", 9000, 6800, 3.4, 3, 1.5, support=True, desc="Opens and keeps open the movement corridors everything else uses.")
unit("counter_ied",         "Counter-IED",           "CoreGround", "Other", 8, 4, 22, 18, 1, 0.4, 1.8, 45, 80, 66, 62, 35, "Demolition charges", 400, 3200, 1.8, 3, 1.1, support=True, desc="Detection and defeat of improvised explosive devices.")
unit("mine_clearance",      "Mine Clearance",        "CoreGround", "Other", 10, 6, 24, 20, 1, 0.5, 1.4, 55, 72, 62, 58, 22, "Mine-clearing charges", 600, 7200, 3.8, 3, 1.6, support=True, desc="Breaches minefields and explosive obstacles.")
unit("mine_laying",         "Mine Laying",           "CoreGround", "Other", 8, 12, 22, 12, 1, 0.5, 1.2, 50, 66, 60, 56, 30, "Scatterable mines", 2400, 5600, 2.8, 3, 1.5, support=True, desc="Counter-mobility. Turns open ground into ground nobody crosses.")
unit("construction_engineer", "Construction Engineer", "CoreGround", "Other", 4, 2, 16, 8, 1, 0.3, 1.0, 110, 56, 56, 54, 22, "5.56mm NATO", 5000, 14000, 5.2, 6, 2.2, support=True, desc="Roads, airstrips, fortifications and base infrastructure.")
unit("geospatial_engineer", "Geospatial Engineer",   "CoreGround", "Other", 2, 1, 12, 5, 1, 0.2, 2.0, 25, 78, 60, 58, 38, "5.56mm NATO",   1500,  1800,  1.4, 4, 0.9, support=True, desc="Terrain analysis and mapping. Decides where the force can actually go.")
unit("eod",                 "EOD",                   "CoreGround", "Other", 8, 4, 20, 10, 1, 0.4, 1.5, 25, 78, 68, 62, 35, "Demolition charges", 300, 1400,  1.4, 3, 0.8, support=True, desc="Explosive ordnance disposal teams.")
unit("cbrn",                "CBRN",                  "CoreGround", "Other", 6, 2, 22, 12, 1, 0.4, 1.5, 40, 70, 62, 58, 32, "5.56mm NATO",   9000,  2600,  1.8, 3, 1.2, support=True, desc="CBRN reconnaissance and decontamination.")
unit("smoke_obscuration",   "Smoke / Obscuration",   "CoreGround", "Other", 4, 2, 18, 10, 1, 2.0, 1.2, 50, 64, 60, 56, 36, "Smoke generators/rounds", 1800, 4800, 2.4, 3, 1.4, support=True, desc="Screens movement and blinds observation across a whole frontage.")
unit("camouflage_deception", "Camouflage and Deception", "CoreGround", "Other", 3, 1, 20, 8, 1, 0.3, 1.5, 45, 70, 62, 58, 34, "5.56mm NATO", 3000, 3600, 2.0, 3, 1.3, support=True, desc="Decoys and signature management. Makes the enemy shoot at nothing.")
unit("military_police",     "Military Police",       "CoreGround", "Other", 18, 4, 30, 8, 2, 0.6, 2.0, 55, 60, 62, 60, 50, "9mm/5.56mm",    15000, 2600,  1.4, 3, 0.9, support=True, desc="Route control, security, detention, rear-area operations.")
unit("intelligence",        "Intelligence",          "CoreGround", "Other", 3, 1, 14, 5, 1, 0.3, 7.0, 35, 80, 64, 60, 35, "5.56mm NATO",   4000,  1800,  1.4, 3, 1.1, support=True, desc="Analysis and fusion. Improves spotting for the whole force.")
unit("humint",              "HUMINT",                "CoreGround", "Other", 6, 2, 14, 4, 1, 0.4, 3.0, 20, 84, 66, 60, 45, "9mm",           1200,  1600,  1.2, 4, 0.7, support=True, desc="Human intelligence collection. Sees intent, not just equipment.")
unit("counter_intelligence", "Counter-Intelligence", "CoreGround", "Other", 6, 2, 16, 5, 1, 0.4, 2.5, 22, 86, 68, 62, 45, "9mm",           1200,  1600,  1.2, 4, 0.7, support=True, desc="Finds and disrupts the enemy's collection against you.")
unit("geoint",              "GEOINT",                "CoreGround", "Other", 2, 1, 12, 5, 1, 0.2, 40.0, 28, 82, 62, 58, 35, "5.56mm NATO",  2000,  1800,  1.4, 3, 1.1, support=True, desc="Imagery and geospatial intelligence exploitation.")
unit("sigint",              "Signals-intelligence",  "CoreGround", "Other", 2, 1, 14, 5, 2, 45.0, 45.0, 35, 80, 62, 58, 32, "5.56mm NATO",  4000,  2200,  1.6, 3, 1.3, support=True, desc="Intercept and geolocation of enemy emitters.")
unit("battlefield_surveillance", "Battlefield Surveillance", "CoreGround", "Other", 8, 4, 20, 10, 3, 1.0, 12.0, 40, 78, 66, 62, 40, "5.56mm + 12.7mm", 8000, 3400, 2.0, 4, 1.1, support=True, desc="Persistent observation of a named area of interest.")
unit("surveillance_radar",  "Ground Surveillance Radar", "CoreGround", "Other", 1, 0, 14, 6, 3, 40.0, 40.0, 25, 76, 60, 56, 34, "5.56mm NATO", 2000, 2200, 1.6, 3, 1.2, support=True, desc="Detects ground movement through weather and darkness.")
unit("electronic_warfare",  "Electronic Warfare",    "CoreGround", "Other", 4, 2, 16, 6, 15, 30.0, 3.0, 45, 74, 62, 58, 38, "5.56mm NATO",  6000,  2800,  1.8, 3, 1.5, cuas=True, support=True, desc="Jamming, direction finding and electronic attack.")
unit("ew_unit",             "Counter-UAS EW",        "CoreGround", "Other", 4, 2, 16, 6, 30, 30.0, 3.0, 40, 75, 62, 58, 35, "5.56mm NATO",  5000,  2600,  1.8, 3, 1.5, cuas=True, support=True, desc="EW element focused on the UAS fight: link and GNSS denial.")
unit("cyber_defence",       "Cyber Defence",         "CoreGround", "Other", 1, 0, 12, 4, 1, 0.1, 1.0, 30, 90, 66, 68, 35, "None (protected)", 0, 1800,  1.2, 4, 1.2, support=True, desc="Protects networks and C2. Invisible until the day it fails.")
unit("cyber_operations",    "Cyberspace Operations", "CoreGround", "Other", 2, 1, 12, 4, 1, 0.1, 1.0, 28, 92, 66, 66, 35, "None (protected)", 0, 1800,  1.2, 4, 1.2, support=True, desc="Offensive cyber effects against enemy systems.")
unit("psyops",              "Psychological Operations", "CoreGround", "Other", 2, 1, 12, 5, 1, 0.3, 2.0, 30, 76, 64, 60, 42, "5.56mm NATO", 2000, 2400, 1.6, 4, 1.0, support=True, desc="Influence operations against enemy and civilian morale.")
unit("cimic",               "Civil-Military Cooperation", "CoreGround", "Other", 2, 1, 12, 5, 1, 0.3, 2.0, 28, 74, 64, 62, 45, "9mm",        1500,  2200,  1.5, 4, 1.0, support=True, desc="Works the civilian side of the battlespace so the rear stays quiet.")
unit("public_affairs",      "Public Affairs",        "CoreGround", "Other", 1, 0, 10, 4, 1, 0.2, 1.5, 18, 70, 62, 60, 45, "9mm",           800,   1600,  1.3, 4, 0.8, support=True, desc="Military information and media handling.")
unit("meteorological",      "Meteorological",        "CoreGround", "Other", 1, 0, 10, 4, 1, 0.2, 3.0, 15, 74, 60, 58, 38, "5.56mm NATO",   800,   1400,  1.2, 4, 0.7, support=True, desc="Weather and atmospherics for aviation and ballistic corrections.")
unit("space_support",       "Space Support",         "CoreGround", "Other", 1, 0, 12, 5, 1, 0.1, 200.0, 25, 90, 64, 62, 30, "None (protected)", 0, 2600, 1.6, 5, 1.6, support=True, desc="Space-enabled ISR, navigation and communications support.")

# --- integrity checks: a duplicate id would silently shadow a unit at runtime,
# and an unknown branch/category would fall through to a default in C#.
CATEGORIES = {"CoreGround", "Drone", "Air", "Naval"}
BRANCHES = {"Infantry", "Armour", "Mechanised", "Artillery", "AntiAircraft",
            "Air", "Navy", "Logistics", "Other"}

seen = set()
for u in U:
    assert u["id"] not in seen, "duplicate unit id: " + u["id"]
    seen.add(u["id"])
    assert u["category"] in CATEGORIES, "bad category on " + u["id"]
    assert u["branch"] in BRANCHES, "bad branch on " + u["id"]

out = os.path.join(os.path.dirname(__file__), "..", "Assets", "StreamingAssets", "Data", "units.json")
with open(out, "w") as f:
    json.dump({"units": U}, f, indent=2)
print(f"Wrote {len(U)} units -> {os.path.abspath(out)}")
