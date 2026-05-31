using UnityEngine;

/// <summary>Minimal on-screen scoreboard + controls help, drawn with IMGUI.</summary>
public class Hud : MonoBehaviour
{
    GUIStyle big, small, status;

    void InitStyles()
    {
        big = new GUIStyle(GUI.skin.label)   { fontSize = 26, fontStyle = FontStyle.Bold };
        small = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        status = new GUIStyle(GUI.skin.label){ fontSize = 20, fontStyle = FontStyle.Bold,
                                               alignment = TextAnchor.UpperCenter };
        big.normal.textColor = small.normal.textColor = status.normal.textColor = Color.white;
    }

    void OnGUI()
    {
        var mm = MatchManager.I;
        if (mm == null) return;
        if (big == null) InitStyles();

        GUI.Label(new Rect(20, 14, 600, 40),
            $"HOME {mm.scoreHome}   :   {mm.scoreAway} AWAY", big);

        GUI.Label(new Rect(0, 56, Screen.width, 30), mm.statusLine, status);

        GUI.Label(new Rect(20, Screen.height - 70, 700, 60),
            "Move: WASD / Arrows   •   Throw: hold LEFT-MOUSE, drag BACK (slingshot), release\n" +
            "Curve: hold A / D while aiming   •   You control the white-ringed player — pass to advance.",
            small);
    }
}
