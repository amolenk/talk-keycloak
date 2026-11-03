<#ftl output_format="plainText">
<#--
  This file has been claimed for ownership from @keycloakify/email-native version 260007.0.0.
  To relinquish ownership and restore this file to its original content, run the following command:
  
  $ npx keycloakify own --path "email/text/password-reset.ftl" --revert
-->
${msg("cityPicksPasswordResetTitle")}

${msg("cityPicksPasswordResetIntro", realmName)}

${link}

${msg("cityPicksLinkExpires", linkExpirationFormatter(linkExpiration))}

${msg("cityPicksPasswordResetIgnore")}

--
${msg("cityPicksEmailFooter", realmName)}
