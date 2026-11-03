<#--
  This file has been claimed for ownership from @keycloakify/email-native version 260007.0.0.
  To relinquish ownership and restore this file to its original content, run the following command:
  
  $ npx keycloakify own --path "email/html/executeActions.ftl" --revert
-->
<#outputformat "plainText">
<#assign requiredActionsText><#if requiredActions??><#list requiredActions><#items as reqActionItem>${msg("requiredAction.${reqActionItem}")}<#sep>, </#sep></#items></#list></#if></#assign>
</#outputformat>
<#import "template.ftl" as layout>
<@layout.emailLayout preheader=msg("cityPicksExecuteActionsPreheader")>
<@layout.heading>${msg("cityPicksExecuteActionsTitle")}</@layout.heading>
<@layout.paragraph>${msg("cityPicksExecuteActionsIntro", realmName)}</@layout.paragraph>
<@layout.paragraph><strong>${requiredActionsText}</strong></@layout.paragraph>
<@layout.button href=link label=msg("cityPicksExecuteActionsButton")/>
<@layout.paragraph>${msg("cityPicksLinkExpires", linkExpirationFormatter(linkExpiration))}</@layout.paragraph>
<@layout.paragraph muted=true>${msg("cityPicksExecuteActionsIgnore")}</@layout.paragraph>
<@layout.fallbackLink href=link/>
</@layout.emailLayout>
