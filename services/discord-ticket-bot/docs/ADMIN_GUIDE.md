# Administrator Guide

1. Create six Discord category channels: SUPPORT, APPLICATIONS, REPORTS, APPEALS, TECHNICAL and DEVELOPMENT.
2. Create department staff roles and set their environment role IDs.
3. Configure audit and transcript channels; restrict transcript access to trusted staff.
4. Run /setup-tickets as an administrator.
5. Review /staff-review-queue. Use Claim, Request Review and Escalate in ticket channels.
6. Use /ticket-stats for metrics and /user-history for moderation context.
7. Closing a ticket generates HTML and Markdown transcripts, uploads both to the configured transcript channel and disables further messages from the opener.

Do not grant Administrator to the bot. Keep staff roles least-privileged and test permission overwrites with a non-staff account.
