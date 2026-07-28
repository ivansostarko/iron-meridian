#!/usr/bin/env python3
"""Generates Assets/StreamingAssets/Data/units.json — the unit catalogue.

The same catalogue is used for both teams (User/Blue and Enemy/Red); a team is
assigned when a unit is placed on the map. All values are company-equivalent;
echelon multipliers in Enums.cs scale them at runtime.

Run:  python scripts/generate_units.py
"""
import json, os

# id, name, cat, atk, hard, dfn, arm, aa, wrng, vrng, mp, trn, mor, org, spd,
# ammoType, ammoStock, fuel, fuelKm, food, supply, indirect, cuas, support, desc
U = []

def unit(id, name, cat, atk, hard, dfn, arm, aa, wrng, vrng, mp, trn, mor, org,
         spd, ammoType, ammoStock, fuel, fuelKm, food, supply, indirect=False,
         cuas=False, support=False, desc=""):
    U.append(dict(id=id, name=name, category=cat, description=desc,
                  attack=atk, hardAttack=hard, defence=dfn, armour=arm,
                  antiAir=aa, weaponRangeKm=wrng, viewRangeKm=vrng,
                  manpower=mp, training=trn, morale=mor, organisation=org,
                  speedKmh=spd, ammoType=ammoType, ammoStock=ammoStock,
                  fuelStock=fuel, fuelUsePerKm=fuelKm, foodDays=food,
                  supplyUsePerDay=supply, canIndirectFire=indirect,
                  canCounterUas=cuas, isSupport=support))

# ---------------- Core ground units ----------------
unit("infantry",            "Infantry",              "CoreGround", 30, 5, 40, 5, 8, 0.8, 2.0, 120, 60, 65, 60, 5,  "5.56mm NATO",      42000, 0,     0.0, 3, 1.0, desc="Foot infantry. Strong in rough and urban terrain.")
unit("mech_infantry",       "Mechanised Infantry",   "CoreGround", 45, 25, 50, 35, 12, 1.5, 2.5, 130, 65, 68, 62, 35, "5.56mm + 25mm",    52000, 9000,  4.2, 3, 1.4, desc="Infantry mounted in IFVs, fights mounted or dismounted.")
unit("mot_infantry",        "Motorised Infantry",    "CoreGround", 35, 8, 42, 10, 9, 1.0, 2.2, 125, 62, 66, 61, 55, "5.56mm NATO",      46000, 5200,  2.0, 3, 1.2, desc="Truck/APC-mobile infantry. Fast on roads, light protection.")
unit("armour",              "Armour",                "CoreGround", 60, 85, 55, 90, 10, 3.0, 2.5, 60, 70, 70, 65, 45, "120mm APFSDS/HE",  1600,  22000, 8.5, 3, 2.0, desc="Main battle tanks. Breakthrough and shock element.")
unit("recon",               "Reconnaissance",        "CoreGround", 20, 12, 25, 20, 8, 1.2, 6.0, 45, 75, 70, 70, 65, "7.62mm + 30mm",    18000, 4200,  2.4, 4, 0.8, desc="Screens, scouts and finds the enemy. Very high view range.")
unit("special_forces",      "Special Forces",        "CoreGround", 55, 15, 45, 5, 10, 1.0, 4.5, 40, 95, 90, 80, 8,  "5.56mm/7.62mm",    16000, 0,     0.0, 5, 0.7, desc="Elite raiding, DA and deep reconnaissance element.")
unit("artillery",           "Artillery",             "CoreGround", 75, 30, 15, 8, 2, 24.0, 1.5, 90, 65, 60, 55, 35, "155mm HE/SMART",   960,   6800,  3.0, 3, 2.2, indirect=True, desc="Tube artillery battalion slice. Long-range fires.")
unit("rocket_artillery",    "Rocket Artillery",      "CoreGround", 90, 35, 12, 8, 2, 70.0, 1.5, 80, 68, 62, 55, 40, "227mm GMLRS",      144,   7600,  3.4, 3, 2.6, indirect=True, desc="MLRS. Very long-range saturation and precision fires.")
unit("mortar",              "Mortar",                "CoreGround", 45, 10, 18, 5, 1, 7.0, 1.2, 60, 60, 60, 58, 12, "120mm mortar",     720,   900,   1.0, 3, 1.2, indirect=True, desc="Close-support indirect fires organic to infantry.")
unit("anti_tank",           "Anti-tank",             "CoreGround", 25, 90, 45, 10, 2, 4.0, 2.5, 55, 68, 64, 60, 25, "ATGM (Javelin-class)", 96, 1800, 1.6, 3, 1.0, desc="ATGM teams and tank destroyers. Lethal vs armour.")
unit("air_defence",         "Air Defence",           "CoreGround", 10, 5, 30, 15, 80, 6.0, 5.0, 65, 66, 62, 58, 30, "35mm AHEAD",       4200,  3600,  2.2, 3, 1.4, cuas=True, desc="Gun/short-range AD. Protects manoeuvre units from air and UAS.")
unit("sam",                 "Surface-to-air Missile","CoreGround", 5, 2, 25, 12, 95, 40.0, 8.0, 70, 70, 62, 55, 30, "SAM (NASAMS-class)", 48,  4200,  2.6, 3, 1.8, cuas=True, desc="Medium/long-range SAM battery. Area air denial.")
unit("engineer",            "Engineer",              "CoreGround", 25, 15, 40, 15, 3, 0.8, 1.8, 85, 62, 63, 60, 30, "5.56mm + demolitions", 24000, 4800, 2.4, 3, 1.6, support=True, desc="Mobility, counter-mobility, fortifications and breaching.")
unit("eod",                 "EOD",                   "CoreGround", 8, 4, 20, 10, 1, 0.4, 1.5, 25, 78, 68, 62, 35, "Demolition charges", 300,  1400,  1.4, 3, 0.8, support=True, desc="Explosive ordnance disposal teams.")
unit("cbrn",                "CBRN",                  "CoreGround", 6, 2, 22, 12, 1, 0.4, 1.5, 40, 70, 62, 58, 32, "5.56mm NATO",      9000,  2600,  1.8, 3, 1.2, support=True, desc="CBRN reconnaissance and decontamination.")
unit("military_police",     "Military Police",       "CoreGround", 18, 4, 30, 8, 2, 0.6, 2.0, 55, 60, 62, 60, 50, "9mm/5.56mm",       15000, 2600,  1.4, 3, 0.9, support=True, desc="Route control, security, detention, rear-area operations.")
unit("signals",             "Signals",               "CoreGround", 5, 2, 18, 6, 1, 0.3, 1.2, 50, 66, 60, 58, 40, "5.56mm NATO",      8000,  2400,  1.6, 3, 1.3, support=True, desc="Communications backbone. Enables command and control.")
unit("electronic_warfare",  "Electronic Warfare",    "CoreGround", 4, 2, 16, 6, 15, 30.0, 3.0, 45, 74, 62, 58, 38, "5.56mm NATO",      6000,  2800,  1.8, 3, 1.5, cuas=True, support=True, desc="Jamming, direction finding and electronic attack.")
unit("intelligence",        "Intelligence",          "CoreGround", 3, 1, 14, 5, 1, 0.3, 7.0, 35, 80, 64, 60, 35, "5.56mm NATO",      4000,  1800,  1.4, 3, 1.1, support=True, desc="Analysis and fusion. Improves spotting for the whole force.")
unit("headquarters",        "Headquarters",          "CoreGround", 5, 2, 20, 10, 2, 0.4, 3.0, 75, 72, 68, 70, 35, "5.56mm NATO",      9000,  3600,  2.0, 4, 2.0, support=True, desc="Command element. Boosts organisation of nearby units.")
unit("logistics",           "Logistics",             "CoreGround", 3, 1, 12, 5, 1, 0.3, 1.0, 95, 55, 58, 55, 45, "5.56mm NATO",      8000,  16000, 2.8, 6, 2.4, support=True, desc="Sustainment planning and distribution node.")
unit("supply",              "Supply",                "CoreGround", 2, 1, 10, 4, 1, 0.3, 1.0, 80, 52, 56, 54, 45, "5.56mm NATO",      6000,  14000, 2.6, 8, 2.2, support=True, desc="Ammunition, fuel and rations resupply point.")
unit("transport",           "Transport",             "CoreGround", 2, 1, 10, 4, 1, 0.3, 1.0, 70, 52, 56, 54, 60, "5.56mm NATO",      5000,  20000, 3.0, 5, 2.0, support=True, desc="Truck transport. Moves units and freight.")
unit("maintenance",         "Maintenance",           "CoreGround", 2, 1, 12, 5, 1, 0.3, 1.0, 65, 60, 58, 56, 40, "5.56mm NATO",      4500,  5200,  2.2, 4, 1.8, support=True, desc="Repair and recovery of vehicles and equipment.")
unit("medical",             "Medical",               "CoreGround", 1, 0, 12, 5, 1, 0.2, 1.0, 70, 64, 62, 58, 40, "None (protected)", 0,     4200,  2.0, 5, 1.6, support=True, desc="Role 1/2 medical. Reduces losses of nearby units.")

# ---------------- Drone-relevant units ----------------
unit("uas_operator",        "UAS Operator Team",     "Drone", 8, 3, 15, 4, 4, 12.0, 12.0, 12, 78, 66, 60, 8,  "sUAS batteries",   60,   0,    0.0, 3, 0.6, support=True, desc="Small-UAS crews flying quadcopters and fixed-wing sUAS.")
unit("recon_uas",           "Reconnaissance UAS",    "Drone", 4, 2, 10, 3, 2, 50.0, 50.0, 25, 76, 64, 58, 15, "UAS airframes",    18,   1200, 1.0, 3, 1.0, support=True, desc="Tactical ISR drones. Deep, persistent surveillance.")
unit("armed_uas",           "Armed UAS",             "Drone", 45, 40, 12, 4, 3, 60.0, 45.0, 30, 80, 66, 58, 15, "Guided munitions", 36,   1600, 1.2, 3, 1.4, indirect=True, desc="MALE/attack drones with guided munitions.")
unit("loitering_munition",  "Loitering-munition Unit","Drone", 55, 60, 10, 3, 2, 40.0, 25.0, 22, 74, 64, 56, 15, "Loitering munitions", 48, 900, 0.8, 3, 1.3, indirect=True, desc="One-way attack drones. High lethality vs vehicles and guns.")
unit("counter_uas",         "Counter-UAS",           "Drone", 6, 2, 22, 10, 70, 8.0, 6.0, 35, 72, 62, 58, 30, "C-UAS effectors",  600,  2400, 1.8, 3, 1.2, cuas=True, desc="Dedicated drone-defeat: jammers, guns, interceptors.")
unit("ad_radar",            "Air-defence Radar",     "Drone", 1, 0, 14, 8, 25, 60.0, 60.0, 30, 70, 60, 55, 28, "5.56mm NATO",      3000, 2600, 1.8, 3, 1.4, cuas=True, support=True, desc="Surveillance radar. Extends air picture for AD and C-UAS.")
unit("ew_unit",             "Electronic-warfare Unit","Drone", 4, 2, 16, 6, 30, 30.0, 3.0, 40, 75, 62, 58, 35, "5.56mm NATO",      5000, 2600, 1.8, 3, 1.5, cuas=True, support=True, desc="EW element focused on the UAS fight: link and GNSS denial.")
unit("sigint",              "Signals-intelligence",  "Drone", 2, 1, 14, 5, 2, 45.0, 45.0, 35, 80, 62, 58, 32, "5.56mm NATO",      4000, 2200, 1.6, 3, 1.3, support=True, desc="Intercept and geolocation of enemy emitters.")
unit("forward_observer",    "Forward Observer",      "Drone", 10, 4, 20, 5, 3, 1.0, 8.0, 10, 82, 70, 62, 8,  "5.56mm NATO",      3000, 0,    0.0, 4, 0.5, support=True, desc="Eyes for the guns. Sharply improves artillery accuracy.")
unit("target_acquisition",  "Target-acquisition Unit","Drone", 3, 1, 15, 6, 4, 30.0, 30.0, 30, 78, 62, 58, 30, "5.56mm NATO",      3500, 2400, 1.6, 3, 1.2, support=True, desc="Counter-battery radar and acoustic sensors.")
unit("joint_fires",         "Joint Fires Cell",      "Drone", 5, 2, 16, 6, 2, 0.5, 5.0, 20, 85, 66, 62, 30, "5.56mm NATO",      2500, 1800, 1.4, 3, 1.1, support=True, desc="Coordinates artillery, UAS and air. Fires multiplier.")
unit("tactical_cp",         "Tactical Command Post", "Drone", 4, 2, 18, 8, 2, 0.5, 4.0, 35, 76, 66, 68, 30, "5.56mm NATO",      4000, 2600, 1.8, 4, 1.6, support=True, desc="Forward C2 node for the drone/fires fight.")

out = os.path.join(os.path.dirname(__file__), "..", "Assets", "StreamingAssets", "Data", "units.json")
with open(out, "w") as f:
    json.dump({"units": U}, f, indent=2)
print(f"Wrote {len(U)} units -> {os.path.abspath(out)}")
