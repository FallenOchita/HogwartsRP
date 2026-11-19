# Ticket Workflows

## Ticket creation

```mermaid
flowchart TD
 A[Member opens department panel] --> B[Select ticket type]
 B --> C[Bot presents modal]
 C --> D[Validate cooldown, quota and duplicate]
 D --> E[Reserve persistent user ticket slot]
 E --> F[Create private Discord channel]
 F --> G[Persist Ticket document]
 G --> H[Post intake and staff controls]
 H --> I[Audit event and confirmation]
```

## Staff handling and closure

```mermaid
flowchart TD
 A[Ticket open] --> B{Staff action}
 B --> C[Claim or assign]
 B --> D[Request review]
 B --> E[Escalate priority]
 B --> F[Approve / deny / revision]
 C --> G[Respond to member]
 D --> G
 E --> G
 F --> G
 G --> H[Close]
 H --> I[Generate HTML and Markdown]
 I --> J[Upload transcript to log channel]
 J --> K[Mark closed and revoke opener send permission]
```

## Permission model

- @everyone: denied ViewChannel.
- Ticket opener: ViewChannel, SendMessages, ReadMessageHistory, AttachFiles, EmbedLinks.
- Configured department role: ViewChannel, SendMessages, ReadMessageHistory, AttachFiles.
- Assigned staff: explicit channel overwrite.
- Bot: ViewChannel, SendMessages, ReadMessageHistory, ManageChannels, AttachFiles.
- Discord administrators retain platform-level administrator bypass.
