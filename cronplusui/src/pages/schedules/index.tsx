import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import PageHead from "@/components/shared/page-head";
import { useDatabase } from "@/hooks/use-database";
import { TriggerType } from "@/types/taskConfig";
import { PlusIcon } from "lucide-react";

export default function SchedulesPage() {
  const { taskConfigs, isLoading, error, refreshTaskConfigs } = useDatabase();
  // We'll use this state later for implementing the create schedule form
  // For now just removing it to fix lint errors
  
  // Filter task configs with TriggerType.Time (cron schedules)
  const schedules = taskConfigs.filter(config => config.triggerType === TriggerType.Time);

  const handleCreateSchedule = () => {
    // We'll implement this later when adding the create form functionality
    alert("Create schedule functionality will be implemented in the next phase");
  };

  return (
    <>
      <PageHead title="Schedules | CronPlus" />
      <div className="max-h-screen flex-1 space-y-4 overflow-y-auto p-4 pt-6 md:p-8">
        <div className="flex items-center justify-between space-y-2">
          <h2 className="text-3xl font-bold tracking-tight">Schedules</h2>
          <Button onClick={handleCreateSchedule} className="flex items-center gap-2">
            <PlusIcon className="size-4" />
            Create Schedule
          </Button>
        </div>

        {error && (
          <Card className="border-red-500 bg-red-50 dark:bg-red-950/30">
            <CardHeader className="pb-2">
              <CardTitle className="text-red-600 dark:text-red-400">Error</CardTitle>
            </CardHeader>
            <CardContent>
              <p>{error}</p>
              <Button 
                variant="outline" 
                className="mt-2"
                onClick={() => refreshTaskConfigs()}
              >
                Retry
              </Button>
            </CardContent>
          </Card>
        )}

        {isLoading ? (
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
            {[1, 2, 3].map(i => (
              <Card key={i} className="animate-pulse">
                <CardHeader className="pb-2">
                  <div className="h-6 w-3/4 rounded-md bg-muted"></div>
                  <div className="h-4 w-1/2 rounded-md bg-muted"></div>
                </CardHeader>
                <CardContent>
                  <div className="h-20 rounded-md bg-muted"></div>
                </CardContent>
              </Card>
            ))}
          </div>
        ) : schedules.length > 0 ? (
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
            {schedules.map(schedule => (
              <Card key={schedule.id} className="cursor-pointer hover:shadow-md transition-shadow">
                <CardHeader className="pb-2">
                  <CardTitle>{schedule.taskType}</CardTitle>
                  <CardDescription>
                    Schedule: {schedule.time || "Not specified"}
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  <div className="space-y-2">
                    <p><span className="font-medium">Directory:</span> {schedule.directory}</p>
                    {schedule.sourceFile && (
                      <p><span className="font-medium">Source:</span> {schedule.sourceFile}</p>
                    )}
                    {schedule.destinationFile && (
                      <p><span className="font-medium">Destination:</span> {schedule.destinationFile}</p>
                    )}
                  </div>
                </CardContent>
              </Card>
            ))}
          </div>
        ) : (
          <Card>
            <CardHeader className="pb-2">
              <CardTitle>No schedules found</CardTitle>
              <CardDescription>
                Create your first schedule to get started
              </CardDescription>
            </CardHeader>
            <CardContent>
              <Button 
                variant="outline" 
                className="flex items-center gap-2"
                onClick={handleCreateSchedule}
              >
                <PlusIcon className="size-4" />
                Create Schedule
              </Button>
            </CardContent>
          </Card>
        )}
      </div>
    </>
  );
}
