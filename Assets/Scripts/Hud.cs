using UnityEngine;

/// <summary>Minimal on-screen scoreboard, throw power meter, and controls help, drawn
/// with IMGUI.</summary>
public class Hud : MonoBehaviour
{
    GUIStyle big, small, status;
    HumanController human;

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
        if (human == null) human = FindAnyObjectByType<HumanController>();

        GUI.Label(new Rect(20, 14, 600, 40),
            $"HOME {mm.scoreHome}   :   {mm.scoreAway} AWAY", big);

        GUI.Label(new Rect(0, 56, Screen.width, 30), mm.statusLine, status);

        // stall count — only while a defender is marking the holder; reddens as it climbs
        if (mm.HolderMarked)
        {
            var prev = GUI.color;
            GUI.color = Color.Lerp(Color.white, new Color(1f, 0.3f, 0.2f),
                                   mm.StallNumber / (float)mm.StallMax);
            GUI.Label(new Rect(0, 88, Screen.width, 34), $"STALL  {mm.StallNumber}", status);
            GUI.color = prev;
        }

        // throw wind-up: power meter + type while charging, button prompt otherwise
        if (human != null)
        {
            float ch = human.ThrowCharge;
            if (ch >= 0f)
            {
                const float w = 240f, h = 16f;
                float x = (Screen.width - w) * 0.5f;
                float y = Screen.height - 116f;
                var prev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, h + 4), Texture2D.whiteTexture);
                GUI.color = Color.Lerp(new Color(0.4f, 0.9f, 0.4f), new Color(0.95f, 0.4f, 0.3f), ch);
                GUI.DrawTexture(new Rect(x, y, w * ch, h), Texture2D.whiteTexture);
                GUI.color = prev;

                GUI.Label(new Rect(0, y - 24f, Screen.width, 22f), human.ThrowTypeLabel, status);
            }
            else if (human.HoldingDisc)
            {
                GUI.Label(new Rect(0, Screen.height - 140f, Screen.width, 24f),
                    "A / D = curve    LEFT = backhand · RIGHT = flick    (tap = fake · hold = throw)", status);
            }
        }

        GUI.Label(new Rect(20, Screen.height - 90, 760, 80),
            "Move: WASD / Arrows   •   Aim: mouse — left/right turns, up/down sets loft\n" +
            "Throw: LEFT = backhand / RIGHT = flick — tap = fake, hold = throw (power)   •   A / D curve\n" +
            "Jump: SPACE   •   Layout (dive): SHIFT / F   •   you control the white-ringed player",
            small);
    }
}
