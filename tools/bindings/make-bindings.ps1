# Writes the SteamVR Input default bindings shipped with the mod (deploy\BepInEx\plugins\TaleSpireVR\SteamVR\
# bindings_<controller>.json) for every supported controller type. SteamVR uses them until the player changes a binding
# in SteamVR's own Controller Bindings screen (like any SteamVR Input game). Actions: actions.json in the same folder.
#   powershell -ExecutionPolicy Bypass -File VRMod\tools\bindings\make-bindings.ps1
$ErrorActionPreference = "Stop"
$out = Join-Path $PSScriptRoot "..\..\deploy\BepInEx\plugins\TaleSpireVR\SteamVR"
$A = "/actions/talespire/in/"

function Src($path, $mode, $inputs, $params) {
    $o = [ordered]@{ inputs = [ordered]@{} }
    foreach ($k in $inputs.Keys) { $o.inputs[$k] = @{ output = $A + $inputs[$k] } }
    $o.mode = $mode
    if ($params) { $o.parameters = $params }
    $o.path = $path
    return $o
}
function LeftIn($p) { "/user/hand/left/input/$p" }
function RightIn($p) { "/user/hand/right/input/$p" }

function Write-Bindings($type, $title, $sources) {
    $doc = [ordered]@{
        action_manifest_version = 0
        alias_info = @{}
        bindings = [ordered]@{
            "/actions/talespire" = [ordered]@{
                chords = @()
                haptics = @(
                    @{ output = "/actions/talespire/out/haptic"; path = "/user/hand/left/output/haptic" },
                    @{ output = "/actions/talespire/out/haptic"; path = "/user/hand/right/output/haptic" })
                poses = @(
                    @{ output = $A + "poseleft"; path = "/user/hand/left/pose/tip" },
                    @{ output = $A + "poseright"; path = "/user/hand/right/pose/tip" })
                skeleton = @(
                    @{ output = $A + "skeletonleft"; path = "/user/hand/left/input/skeleton/left" },
                    @{ output = $A + "skeletonright"; path = "/user/hand/right/input/skeleton/right" })
                sources = $sources
            }
        }
        category = "steamvr_input"
        controller_type = $type
        description = "Default TaleSpire VR bindings for $title"
        name = "TaleSpire VR - $title defaults"
        options = @{}
        simulated_actions = @()
    }
    $file = Join-Path $out "bindings_$type.json"
    ($doc | ConvertTo-Json -Depth 12) | Set-Content -Encoding utf8 $file
    Write-Host "Wrote $file ($($sources.Count) sources)"
}

# Shared by every controller: triggers (click + how far pulled, for the hands' index fingers).
function Triggers {
    @(
        (Src (LeftIn "trigger") "trigger" @{ click = "triggerleft"; pull = "triggervalue" }),
        (Src (RightIn "trigger") "trigger" @{ click = "triggerright"; pull = "triggervalue" }))
}

# Valve Index: as before (trackpad presses = turn / raise modifiers and ping, firm grip squeeze = measure / lasso).
$index = (Triggers) + @(
    (Src (RightIn "a") "button" @{ click = "rightclick"; touch = "thumbrest" }),
    (Src (RightIn "b") "button" @{ click = "menutoggle"; touch = "thumbrest" }),
    (Src (LeftIn "a") "button" @{ click = "zoomout"; touch = "thumbrest" }),
    (Src (LeftIn "b") "button" @{ click = "zoomin"; touch = "thumbrest" }),
    (Src (LeftIn "thumbstick") "joystick" ([ordered]@{ position = "move"; click = "buildmodetoggle"; touch = "thumbrest" })),
    (Src (RightIn "thumbstick") "joystick" ([ordered]@{ position = "turnheight"; click = "middleclick"; touch = "thumbrest" })),
    (Src (RightIn "trackpad") "button" @{ click = "rotatemodifier"; touch = "thumbrest" }),
    (Src (LeftIn "trackpad") "button" @{ click = "elevatemodifier"; touch = "thumbrest" }),
    (Src (LeftIn "grip") "grab" @{ grab = "measure" } @{ value_hold_threshold = "1.4"; value_release_threshold = "1.3" }),
    (Src (RightIn "grip") "grab" @{ grab = "lasso" } @{ value_hold_threshold = "1.4"; value_release_threshold = "1.3" }),
    (Src (LeftIn "grip") "trigger" @{ pull = "gripvalue" }),
    (Src (RightIn "grip") "trigger" @{ pull = "gripvalue" }))
Write-Bindings "knuckles" "Valve Index controllers" $index

# Thumbstick controllers (Meta Touch, Pico, Vive Focus 3, HP Reverb G2, Vive Cosmos): A / B and X / Y like the Index's
# A / B buttons; no trackpads, so a grip pressed in does the trackpad's job (tap = ping, hold while carrying = turn /
# raise, both for a second = stream camera / Predator Vision) and a grip squeezed all the way is the measure / lasso grip.
function StickController($modifierPath) {
    $s = (Triggers) + @(
        (Src (RightIn "a") "button" @{ click = "rightclick"; touch = "thumbrest" }),
        (Src (RightIn "b") "button" @{ click = "menutoggle"; touch = "thumbrest" }),
        (Src (LeftIn "x") "button" @{ click = "zoomout"; touch = "thumbrest" }),
        (Src (LeftIn "y") "button" @{ click = "zoomin"; touch = "thumbrest" }),
        (Src (LeftIn "joystick") "joystick" ([ordered]@{ position = "move"; click = "buildmodetoggle"; touch = "thumbrest" })),
        (Src (RightIn "joystick") "joystick" ([ordered]@{ position = "turnheight"; click = "middleclick"; touch = "thumbrest" })),
        (Src (LeftIn "grip") "grab" @{ grab = "measure" } @{ value_hold_threshold = "0.9"; value_release_threshold = "0.8" }),
        (Src (RightIn "grip") "grab" @{ grab = "lasso" } @{ value_hold_threshold = "0.9"; value_release_threshold = "0.8" }),
        (Src (LeftIn "thumbrest") "button" @{ touch = "thumbrest" }),
        (Src (RightIn "thumbrest") "button" @{ touch = "thumbrest" }))
    if ($modifierPath) {
        $s += (Src (LeftIn $modifierPath) "button" @{ click = "elevatemodifier" })
        $s += (Src (RightIn $modifierPath) "button" @{ click = "rotatemodifier" })
        $s += (Src (LeftIn "grip") "trigger" @{ pull = "gripvalue" })
        $s += (Src (RightIn "grip") "trigger" @{ pull = "gripvalue" })
    } else {
        $s += (Src (LeftIn "grip") "trigger" @{ click = "elevatemodifier"; pull = "gripvalue" })
        $s += (Src (RightIn "grip") "trigger" @{ click = "rotatemodifier"; pull = "gripvalue" })
    }
    return $s
}
Write-Bindings "oculus_touch" "Meta Touch controllers" (StickController $null)
Write-Bindings "pico_controller" "Pico controllers" (StickController $null)
Write-Bindings "vive_focus3_controller" "HTC Vive Focus 3 controllers" (StickController $null)
Write-Bindings "hpmotioncontroller" "HP Reverb G2 controllers" (StickController $null)
# The Cosmos has bumpers above the triggers: those are the turn / raise modifiers there (the grips stay grips).
Write-Bindings "vive_cosmos_controller" "HTC Vive Cosmos controllers" (StickController "bumper")

# Vive wands: one trackpad, a menu button and a grip button per hand. Trackpad touch = the sticks; its centre press =
# the trackpad gestures (as on the Index); up / down on the left pad = zoom in / out; up on the right pad = A (menus).
$wands = (Triggers) + @(
    (Src (LeftIn "trackpad") "trackpad" ([ordered]@{ position = "move"; touch = "thumbrest" })),
    (Src (RightIn "trackpad") "trackpad" ([ordered]@{ position = "turnheight"; touch = "thumbrest" })),
    (Src (LeftIn "trackpad") "dpad" ([ordered]@{ north = "zoomin"; south = "zoomout"; center = "elevatemodifier" }) @{ sub_mode = "click" }),
    (Src (RightIn "trackpad") "dpad" ([ordered]@{ north = "rightclick"; south = "middleclick"; center = "rotatemodifier" }) @{ sub_mode = "click" }),
    (Src (LeftIn "application_menu") "button" @{ click = "buildmodetoggle" }),
    (Src (RightIn "application_menu") "button" @{ click = "menutoggle" }),
    (Src (LeftIn "grip") "button" @{ click = "measure" }),
    (Src (RightIn "grip") "button" @{ click = "lasso" }))
Write-Bindings "vive_controller" "HTC Vive wands" $wands

# Windows Mixed Reality: thumbstick and trackpad. Sticks as on the Index; trackpad centre press = the trackpad gestures;
# up / down on the left pad = zoom in / out; up on the right pad = A (menus); menu buttons = B and Build Mode.
$wmr = (Triggers) + @(
    (Src (LeftIn "joystick") "joystick" ([ordered]@{ position = "move"; click = "buildmodetoggle"; touch = "thumbrest" })),
    (Src (RightIn "joystick") "joystick" ([ordered]@{ position = "turnheight"; click = "middleclick"; touch = "thumbrest" })),
    (Src (LeftIn "trackpad") "dpad" ([ordered]@{ north = "zoomin"; south = "zoomout"; center = "elevatemodifier" }) @{ sub_mode = "click" }),
    (Src (RightIn "trackpad") "dpad" ([ordered]@{ north = "rightclick"; center = "rotatemodifier" }) @{ sub_mode = "click" }),
    (Src (LeftIn "trackpad") "button" @{ touch = "thumbrest" }),
    (Src (RightIn "trackpad") "button" @{ touch = "thumbrest" }),
    (Src (RightIn "application_menu") "button" @{ click = "menutoggle" }),
    (Src (LeftIn "application_menu") "button" @{ click = "rightclick" }),
    (Src (LeftIn "grip") "button" @{ click = "measure" }),
    (Src (RightIn "grip") "button" @{ click = "lasso" }))
Write-Bindings "holographic_controller" "Windows Mixed Reality controllers" $wmr
