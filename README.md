# MsgHatch
A flexible and extensible .NET library that can generate and handle various message formats - such as emails, SMS or push notifications - based on templates.

# Pipeline
1. An incoming domain event is the trigger.
2. The notification target resolver figures out who to notify about this domain event and on which channel.

For each of these targets
3. The template selector decides which template to use.
4. Then the template data resolver loads all necessary data for the given domain event, notification target and template to build the message data model
5. Then the template renderer builds the message payload based on the template and the message data model
6. Finally the message transport gets the paylod and sends it
