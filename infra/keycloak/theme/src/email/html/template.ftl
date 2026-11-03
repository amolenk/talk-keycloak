<#--
  This file has been claimed for ownership from @keycloakify/email-native version 260007.0.0.
  To relinquish ownership and restore this file to its original content, run the following command:
  
  $ npx keycloakify own --path "email/html/template.ftl" --revert
-->
<#--
  City Picks email layout.
  Every HTML email (owned or still extension-managed) is wrapped in emailLayout, so the
  branding below applies to all of them. Styles are inline and the layout is table based
  because many mail clients ignore <style> blocks and modern CSS layout.
-->

<#macro emailLayout preheader="">
<!DOCTYPE html>
<html lang="${(locale.language)!'en'}">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta name="color-scheme" content="light">
    <title>${realmName}</title>
</head>
<body style="margin:0;padding:0;background-color:#f6f4ee;color:#172d2a;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
    <#if preheader?has_content>
    <div style="display:none;max-height:0;overflow:hidden;opacity:0;">${preheader}</div>
    </#if>
    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:#f6f4ee;">
        <tr>
            <td align="center" style="padding:32px 16px;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;">
                    <tr>
                        <td style="padding:0 0 20px 0;">
                            <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                                <tr>
                                    <td width="40" height="40" align="center" valign="middle" style="width:40px;height:40px;background-color:#23634c;border-radius:10px;color:#ffffff;font-size:22px;font-weight:700;line-height:40px;">+</td>
                                    <td style="padding-left:12px;font-size:22px;font-weight:800;letter-spacing:-0.5px;color:#172d2a;">${realmName}</td>
                                </tr>
                            </table>
                        </td>
                    </tr>
                    <tr>
                        <td class="cp-card" style="background-color:#fffefa;border:1px solid #d7ded6;border-radius:16px;padding:36px 32px;font-size:16px;line-height:1.6;color:#172d2a;">
                            <#nested>
                        </td>
                    </tr>
                    <tr>
                        <td style="padding:20px 8px 0 8px;font-size:13px;line-height:1.5;color:#566562;text-align:center;">
                            ${msg("cityPicksEmailFooter", realmName)}
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>
</#macro>

<#-- Primary call-to-action button. Bulletproof-ish: a padded link inside a coloured cell. -->
<#macro button href label>
<table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:28px 0;">
    <tr>
        <td align="center" bgcolor="#23634c" style="border-radius:8px;">
            <a href="${href}" target="_blank" style="display:inline-block;padding:14px 26px;font-size:16px;font-weight:700;color:#ffffff;text-decoration:none;border-radius:8px;">${label}</a>
        </td>
    </tr>
</table>
</#macro>

<#macro heading>
<h1 style="margin:0 0 16px 0;font-size:24px;line-height:1.3;font-weight:750;color:#172d2a;"><#nested></h1>
</#macro>

<#macro paragraph muted=false>
<p style="margin:0 0 16px 0;<#if muted>color:#566562;font-size:14px;</#if>"><#nested></p>
</#macro>

<#-- Plain URL for clients that block buttons, and for copy/paste. -->
<#macro fallbackLink href>
<p style="margin:24px 0 0 0;padding-top:16px;border-top:1px solid #d7ded6;font-size:13px;color:#566562;">
    ${msg("cityPicksEmailFallbackLink")}<br>
    <a href="${href}" style="color:#23634c;word-break:break-all;">${href}</a>
</p>
</#macro>
