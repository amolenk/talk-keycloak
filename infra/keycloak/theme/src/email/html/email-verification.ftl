<#--
  This file has been claimed for ownership from @keycloakify/email-native version 260007.0.0.
  To relinquish ownership and restore this file to its original content, run the following command:
  
  $ npx keycloakify own --path "email/html/email-verification.ftl" --revert
-->
<#import "template.ftl" as layout>
<@layout.emailLayout preheader=msg("cityPicksEmailVerificationPreheader")>
<@layout.heading>${msg("cityPicksEmailVerificationTitle")}</@layout.heading>
<@layout.paragraph>${msg("cityPicksEmailVerificationIntro", realmName)}</@layout.paragraph>
<@layout.button href=link label=msg("cityPicksEmailVerificationButton")/>
<@layout.paragraph>${msg("cityPicksLinkExpires", linkExpirationFormatter(linkExpiration))}</@layout.paragraph>
<@layout.paragraph muted=true>${msg("cityPicksEmailVerificationIgnore")}</@layout.paragraph>
<@layout.fallbackLink href=link/>
</@layout.emailLayout>
