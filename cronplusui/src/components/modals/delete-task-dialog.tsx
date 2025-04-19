import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle
} from "@/components/ui/alert-dialog";
import { useDatabase } from "@/hooks/use-database";
import { useState } from "react";

interface DeleteTaskDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  taskId?: string;
  taskName?: string;
}

export function DeleteTaskDialog({ 
  open, 
  onOpenChange, 
  taskId,
  taskName 
}: DeleteTaskDialogProps) {
  const { deleteTaskConfig } = useDatabase();
  const [isDeleting, setIsDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleDelete = async () => {
    if (!taskId) return;
    
    setIsDeleting(true);
    setError(null);
    
    try {
      const success = await deleteTaskConfig(taskId);
      
      if (success) {
        onOpenChange(false);
      } else {
        setError("Failed to delete task. Please try again.");
      }
    } catch (error) {
      console.error("Error deleting task:", error);
      setError(`Error: ${error instanceof Error ? error.message : "Unknown error occurred"}`);
    } finally {
      setIsDeleting(false);
    }
  };

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Are you sure you want to delete this task?</AlertDialogTitle>
          <AlertDialogDescription>
            {taskName ? (
              <>This will permanently delete the task <span className="font-semibold">{taskName}</span>.</>
            ) : (
              "This will permanently delete the selected task."
            )}
            <br />
            This action cannot be undone.
          </AlertDialogDescription>
        </AlertDialogHeader>
        
        {error && (
          <div className="bg-red-50 border border-red-400 text-red-700 px-4 py-3 rounded mb-4">
            <p>{error}</p>
          </div>
        )}
        
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isDeleting}>Cancel</AlertDialogCancel>
          <AlertDialogAction 
            onClick={handleDelete}
            disabled={isDeleting}
            className="bg-red-500 hover:bg-red-600"
          >
            {isDeleting ? "Deleting..." : "Delete"}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
