Improvement 1:

When loading the config file, validate the config file and if a task is invalid, disable it and log a warning. But start the application anyway.

[x] Implemented


Improvement 2:

When a task ccannot be started, give an indication in the UI, why it was not started.

[x] Implemented


Improvement 3:

Add a file or folder dialog picker for fields that require a path.

[x] Implemented


Improvement 4:

Add the possibility to add variables (multiple) to tasks.
These variables have a name and a value and a datatype (string, int, bool, date, datetime).
These variables can be used in the pipeline steps as ${variableName}.

[ ] Implemented


Improvement 5:

redesign th edit task page, make it more user friendly.
It should show a header section with the ID, enabled checkbox, and the watch section.
Under that, it should show the variables section.
Under that, it should show the pipeline section.
Put the save, cancel and delete buttons at the top.

[ ] Implemented