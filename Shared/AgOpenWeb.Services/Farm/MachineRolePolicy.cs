// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using AgOpenWeb.Models.Farm;

namespace AgOpenWeb.Services.Farm;

/// <summary>
/// What each machine role may read, forward, or command. Every command flag this
/// module enforces is false. A read-only logger cannot be flipped into a steer,
/// rate, or section path by changing a checkbox: there is no command method to call.
/// Full guidance stays on the existing AgOpen host. This module does not call
/// <c>IAutoSteerService</c> and does not send PGN 254, 239, 229, or 241.
/// </summary>
public static class MachineRolePolicy
{
    public const string SafetyBanner =
        "Planning/simulation only — not connected to machine control.";

    public sealed class Permissions
    {
        public string Role { get; init; } = "";
        public bool MayReadGps { get; init; }
        public bool MayReadPgn250 { get; init; }
        public bool MayReadPgn253 { get; init; }
        public bool MayReadPgn211 { get; init; }
        public bool MayForwardLogs { get; init; }
        public bool MayRecordGuidanceLines { get; init; }
        public bool MayVisualizeGuidance { get; init; }
        public bool MayCommandSteer { get; init; }
        public bool MayCommandRate { get; init; }
        public bool MayCommandSections { get; init; }
        public bool MaySendPgn254 { get; init; }
        public bool MaySendPgn239 { get; init; }
        public bool MaySendPgn229 { get; init; }
        public bool MaySendPgn241 { get; init; }
        public string CommandPath { get; init; } = "";
    }

    public static Permissions For(string? role) => role switch
    {
        MachineRoles.ImplementGuidance => Base(MachineRoles.ImplementGuidance,
            recordLines: true, visualize: true,
            "Planning/simulation only — not connected to machine control. " +
            "Guidance lines may be stored and drawn. Nothing is sent to a display or a valve."),
        MachineRoles.FullGuidance => Base(MachineRoles.FullGuidance,
            recordLines: false, visualize: true,
            "Existing AgOpen guidance host only. This farm module does not call AutoSteerService " +
            "and does not send PGN 254, 239, 229, or 241. Opening the farm office does not arm steer."),
        _ => Base(MachineRoles.ReadOnlyLogger,
            recordLines: false, visualize: false,
            "None. Logging cannot become control. This role may read and forward logs only."),
    };

    private static Permissions Base(string role, bool recordLines, bool visualize, string path) => new()
    {
        Role = role,
        MayReadGps = true,
        MayReadPgn250 = true,
        MayReadPgn253 = true,
        MayReadPgn211 = true,
        MayForwardLogs = true,
        MayRecordGuidanceLines = recordLines,
        MayVisualizeGuidance = visualize,
        MayCommandSteer = false,
        MayCommandRate = false,
        MayCommandSections = false,
        MaySendPgn254 = false,
        MaySendPgn239 = false,
        MaySendPgn229 = false,
        MaySendPgn241 = false,
        CommandPath = path,
    };

    public static IReadOnlyList<Permissions> Matrix() => new[]
    {
        For(MachineRoles.ReadOnlyLogger),
        For(MachineRoles.ImplementGuidance),
        For(MachineRoles.FullGuidance),
    };

    /// <summary>
    /// Null when a sample may be stored. A command PGN or a command kind is refused.
    /// Virtual-terminal keypresses are bus writes and are not accepted as telemetry.
    /// </summary>
    public static string? RefuseInbound(int? pgn, string? kind)
    {
        if (pgn is 254 or 239 or 229 or 241)
            return $"PGN {pgn} is a command. The farm office will not accept it, store it, or forward it.";
        if (kind is "steer-command" or "rate-command" or "section-command"
            or "gps-offset" or "cart-command" or "vt-keypress")
            return $"Refused ({kind}). This module does not command machines, offset GPS, or write the virtual terminal.";
        return null;
    }
}
