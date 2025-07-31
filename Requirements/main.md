## main purpose of the app
Create a cron job that executes certain tasks based on a json config file.

Tasks to implement:
- when a file is created in a certain directory:
    - print the file to a printer
    - archive the printed file to a certain location

- when a file is created in a certain directory:
    - copy the file to a certain location
    - delete the file from the original location
    
    
## architecture
A backend appliction written in Go, that reads the config file and executes the tasks based on the config file.
Each task should run independently of the other tasks.


A frontend application written in Go, that allows the user to create and manage the config file.
