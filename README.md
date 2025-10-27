# Creos.DatabaseMigration

## Project Purpose:
This library project is intended to run postgres scripts for a database-first migration.
This Creos.DatabaseMigration library must be included in the application that is connecting to the database.

## Secondary library/project containing scripts
You have two options here - both work the same from a code perspective.  
1.  You can create a new project within your application to house the database scripts.
2.  You can create an entirely new solution/project that builds a Nuget package.  This nuget package will then be imported into the application.

Either way, the new project will need to follow some conventions for storing the script files.  
This new project will have no code in it.  It will just contain strategically-named files and folders. 
This new project will typically be named like so:  "Creos.Database.xxxxx".
While it is encouraged to conform to this naming convention for the project name, you can control and override this however your team decides.  

The name of this new project will need to be defined when calling the method to execute your scripts.  PropertyName:  "ScriptProjectName"

Examples for the ScriptProjectName:
- Creos.Database.CDA
- Creos.Database.Flowable
- Creos.Database.Authority
- Creos.Database.FNU
- DatabaseMigration.Authority

## Folders and Heirarchy/Ordering for database scripts
There could be potentially three different base folders in your script project:
 - pre
 - {user-defined-folder}
 - post
 
(The folder names are case-insensitive.)

All scripts must have the extension .psql.   There is no exception to this.  


### "Pre" folder
This folder will contain scripts that are re-runnable.  
These files must end in .psql.  
These scripts will execute every time any scripts are executed.  Therefore, these scripts must be re-runnable.  
It is advised to keep these simple and quick as these scripts will be executed frequently - more or less depending upon your implementation. 
Scripts in this folder will be executed prior to any scripts in the User-defined folder. 
Ordering of these scripts are sorted by their names.  If these files are in sub folders under the pre folder, then this folder name is taken into account while determining order.

### User-defined folder
This folder will contain scripts that will only execute once, successfully.  

The file name must be an integer or a decimal number.  This determines order when executing scripts.
Script files ending in .psql but are not int/decimal numbers will be skipped and not executed.  
Decimal numbers can be up to 5 decimal places in length (out to the hundred-thousandths place).

When a particular script succeeds it will save a record (version number and UTC date) into the applicable table into the database. 
This table and schema can be defined by the user.  Default is schema: "public" and table name: dbversion.  

### "Post" folder
Similar to the "Pre" folder, this folder will contain scripts that are re-runnable.  
Main difference is that scripts in this folder will be executed after any scripts in the User-defined folder. 

Example folder structure in your script project:
```
 - PRE
 -- 1 (folder)
 --- FunctionName1.psql (file)
 --- FunctionName2.psql (file)
 --- FunctionName3.psql (file)
 - POST
 -- 1 (folder)
 --- 1.psql (file)
 --- 2.psql (file)
 --- 3.psql (file)
 --- 4.psql (file)
 - SqlFiles (user-defined folder)
 -- 1 (folder)
 --- 1.0001.psql (file)
 --- 1.0002.psql (file)
 --- 1.0003.psql (file)
 -- 2 (folder)
 --- 2.0001.psql (file)
 --- 2.0002.psql (file)
 -- Release_3-2-7
 --- 3.0001.psql (file)
 --- 3.0002.psql (file)
```

You are welcome to insert any additional folders under the three main folders to help keep your scripts organized.  However, all scripts under the three main 
folders must still be unique.  

An example of a voilation that will result in an error is this:
```
 - SqlFiles (user-defined folder)
 -- 1 (folder)
 --- 1.0001.psql (file)
 -- 2 (folder)
 --- 1.0001.psql (file)
```

### base.psql
There is an optional "base.psql" file that can be executed the first time this is executed against a database.  This is mainly for backwards-compatibility.
If the dbversion table has no records in it, then the "base.psql" will be executed if one exists folder. 
This file is the only exception to the decimal naming convention for database script files. 
If the base.psql file is executed, a 0.0 entry is inserted into the VersionTable. 
 
### Script Naming Conventions
 - The postgres script file must end in .psql
 
### Script Execution
 - Scripts will execute in the numerical order of their filename.
 - If a particular script fails to execute, then none of the following scripts will be executed.
 
### Script file properties
 - Each of the .psql files must be marked as *Embedded Resource* and *Do not copy to output directory*
 - Please remember to build your project after adding a file.  Building a project adds a record to the .csproj file.  This is often forgotten and has often resulted in many head-aches.

### Tips
 - Again - Don't forget to build the project after adding a new script file.
 - Do not include any long running scripts to be executed by this project.  (For example: Large indexes)  Long running scripts should be handled differently.

## Required Properties
### ScriptProjectName
 - This defines the project name that contains the script files.
### Connection String List
 - This is a class that defines 1 or more connection strings for connections to the database. 
 - There are two properties in this class:  ConnectionStringName and CnString
 - ConnectionStringName is an optional property.  This defaults to the Database name if not defined.
 - CnString is required.

## Optional/overridable properties
### VersionTable:
  - Default = "dbversion" -- Table that holds the executed version scripts.  If this is overridden after scripts have deployed, all scripts will be redeployed.
### SchemaName:
  - Default: "public" -- Schema for the VersionTable.  If this is overridden after scripts have deployed, all scripts will be redeployed.
### Log_ElapsedSeconds: 
  - Default = 15 seconds -- Every x seconds for long running script, a debug log entry will be generated. 
### Timeout
  - Default = 30 -- CommandTimeout for a particular script.
### FolderNameWithAllScripts
  - Default = "SqlFiles" -- This is the user-defined folder containing all scripts that are to be executed only once. 
### MaxThreads_Concurrency
  - Default = 1 -- This defines how many connection will concurrently execute their applicable scripts.  Please be careful to not set this to an unreasably high number. 
  




  
  
## Examples:

### Hosted Service Example:

This example demonstrates implementing this into a .NET HostedService.
```
    internal class DatabaseMigrationHostedService : IHostedService
    {
        private readonly ILogger<DatabaseMigrationHostedService> _logger;
        private readonly IConfiguration _configuration;
        public DatabaseMigrationHostedService(ILogger<DatabaseMigrationHostedService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {

            var migrationHelper = new DatabaseMigrationHelper(_logger);
            var migrationResults = await migrationHelper.UpdateDatabaseAsync(new DatabaseMigrationModel
            {
                ConnectionStrings = new List<ConnectionStringInfo> { new(_configuration.GetSection("ConnectionStrings:TestCreos").Value), new ConnectionStringInfo(_configuration.GetSection("ConnectionStrings:TestCreos2").Value) },
                ScriptProjectName = "Creos.database.test",
                FolderNameWithAllScripts = "VersionScripts",
                MaxThreads_Concurrency = 2
            }, cancellationToken);

            _logger.LogDebug("Overall Success: {success}", migrationResults.OverallSuccess);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

```

Keep in mind that this can be implemented wherever your business logic sees fit.  
For example, it may be a common to put this into a controller so execution is triggered elsewhere. 

