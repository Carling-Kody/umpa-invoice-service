using System.Text;
using UmpaInvoiceService.Intacct;

/// <summary>
/// Translates common Sage Intacct errors into plain-English, actionable guidance for this sample.
/// Kept in the sample (not the library) because it's operator-facing troubleshooting, not client logic.
/// </summary>
internal static class Diagnostics
{
    /// <summary>Explains a thrown transport/auth failure.</summary>
    // ex.Message includes the joined structured errors (errorno | description | ...).
    public static string Explain(IntacctException ex, IntacctClientOptions options)
        => Explain(ex.Message, options);

    /// <summary>
    /// Explains a raw Intacct error string. Used for function-level failures, which come back in a
    /// successful HTTP response as a failed &lt;result&gt; rather than as a thrown exception.
    /// </summary>
    public static string Explain(string text, IntacctClientOptions options)
    {
        bool Has(string needle) => text.Contains(needle, StringComparison.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("Diagnosis:");

        if (Has("GW-0011") || Has("Invalid request"))
        {
            // Control-level rejection: the gateway didn't accept the sender credentials/envelope.
            sb.AppendLine("  → SENDER credential problem (the request was rejected before login).");
            sb.AppendLine("    • Double-check SenderId and SenderPassword in appsettings.json.");
            sb.AppendLine($"    • Confirm the Sender ID is authorized for company '{options.CompanyId}'.");
        }
        else if (Has("XL03000006") || Has("Login information is incorrect"))
        {
            // Authentication-level: sender creds were accepted, but the company/user login failed.
            sb.AppendLine("  → LOGIN credential problem (Sender ID was accepted; the user login failed).");
            sb.AppendLine($"    • Verify CompanyId ({options.CompanyId}), UserId ({options.UserId}), and UserPassword.");
            sb.AppendLine("    • Note: a Web Services user CANNOT log into the Intacct web UI, so you can't");
            sb.AppendLine("      isolate this by trying a UI login — the only way to test it is this API call.");
            sb.AppendLine("    • Confirm the user is listed under:");
            sb.AppendLine("        Company → Admin → Users, roles, and groups → Web Services users");
            sb.AppendLine("    • Web Services passwords do not expire, so an expiry is not the cause.");
            sb.AppendLine("    • Also check for stray spaces or quotes around the values in appsettings.json.");
        }
        else if (Has("PL04000005") || (Has("do not have permission") && Has("API operation")))
        {
            // Authorization: login succeeded, but the user isn't permitted for this object/operation.
            sb.AppendLine("  → PERMISSION problem (login succeeded; the user is not authorized for this object).");
            sb.AppendLine("    Two separate layers both have to allow it:");
            sb.AppendLine($"    1. USER TYPE — caps the maximum features available to {options.UserId},");
            sb.AppendLine("       independent of role. Sage documents 'Business user' as the type with full");
            sb.AppendLine("       feature access; other types have limited access. Whether an 'Employee' type");
            sb.AppendLine("       specifically blocks AR/GL objects is NOT documented — treat it as a lead to");
            sb.AppendLine("       check with Sage, not a confirmed cause.");
            sb.AppendLine("    2. ROLE PERMISSIONS — what the assigned role actually grants within that cap.");
            sb.AppendLine("    Check both at:");
            sb.AppendLine("        Company → Admin → Users, roles, and groups → Web Services users → Edit");
            sb.AppendLine("    START WITH LAYER 2. A role granting no API access looks exactly like a user-type");
            sb.AppendLine("    cap: every object fails identically. 'Fails everywhere' does NOT mean user type.");
            sb.AppendLine("    History: this blocked UMPA from 2026-07 to 2026-09. We chased user type for weeks;");
            sb.AppendLine("    the actual fix was adding object permissions to the role assigned to the web");
            sb.AppendLine("    services user. Check the ASSIGNED role's object list first, and confirm the role");
            sb.AppendLine("    you are editing is the one actually assigned to this user.");
        }
        else if (Has("not authorized") && Has("sender"))
        {
            sb.AppendLine("  → The Sender ID is not authorized for this company.");
            sb.AppendLine("    • Intacct: Company → Setup → Web Services authorizations → add the Sender ID.");
        }
        else if (Has("web services") || Has("not a web services") || Has("XL03000009"))
        {
            sb.AppendLine("  → The login user may not be permitted for Web Services access.");
            sb.AppendLine($"    • Make {options.UserId} a 'Web Services' user type, or grant it web services access + permissions.");
        }
        else if (Has("HTTP "))
        {
            sb.AppendLine("  → Transport/gateway error (not a normal Intacct response).");
            sb.AppendLine("    • Check network access to api.intacct.com and that the endpoint URL is correct.");
        }
        else
        {
            sb.AppendLine("  → Unrecognized error. Look up the error number above in Sage's documentation,");
            sb.AppendLine("    or share the full message with Sage support (include the Support ID if present).");
        }

        return sb.ToString().TrimEnd();
    }
}
