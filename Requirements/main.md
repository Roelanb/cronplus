## main purpose of the app
Create a cron job executor that executes certain tasks.

Tasks to implement:
- when a file is created in a certain directory:
    - print the file to a printer
    - archive the printed file to a certain location

- when a file is created in a certain directory:
    - copy the file to a certain location
    - delete the file from the original location

- when a file is created in a certain directory:
    - post the content of the file to a rest endpoint

So task can exist of multiple steps that are executed in sequence.
Implement also a decision step that can be used to execute a step based on a condition.

    
## architecture
A backend appliction written in C#, that reads the config file and executes the tasks based on the configuration.

Use DuckDB to store the configuration.
Tasks, steps, condition, execution log etc should be stoed

Each task should run independently of the other tasks.
Use dotnet 9.
Code should be structured in a way that it is easy to maintain and extend.
Put in a folder: backend


A frontend application written in react/vite to maintain the task and step configuration.
Clean user friendly interface.
Use antd for the ui.
Use typescript
Put in a folder: frontend
