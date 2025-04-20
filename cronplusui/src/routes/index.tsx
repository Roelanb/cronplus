import FormPage from '@/pages/form';
import NotFound from '@/pages/not-found';
import { Suspense, lazy } from 'react';
import { Navigate, Outlet, useRoutes } from 'react-router-dom';

const DashboardLayout = lazy(
  () => import('@/components/layout/dashboard-layout')
);
const SignInPage = lazy(() => import('@/pages/auth/signin'));
const TasksPage = lazy(() => import('@/pages/tasks'));
const TaskLogsPage = lazy(() => import('@/pages/task-logs'));
const OneTimeTasksPage = lazy(() => import('@/pages/one-time-tasks'));

// ----------------------------------------------------------------------

export default function AppRouter() {
  const dashboardRoutes = [
    {
      path: '/',
      element: (
        <DashboardLayout>
          <Suspense>
            <Outlet />
          </Suspense>
        </DashboardLayout>
      ),
      children: [
        {
          element: <Navigate to="/tasks" replace />,
          index: true
        },
        {
          path: 'tasks',
          element: <TasksPage />
        },
        {
          path: 'task-logs',
          element: <TaskLogsPage />
        },
        {
          path: 'one-time-tasks',
          element: <OneTimeTasksPage />
        },
        {
          path: 'form',
          element: <FormPage />
        }
      ]
    }
  ];

  const publicRoutes = [
    {
      path: '/login',
      element: <SignInPage />,
      index: true
    },
    {
      path: '/404',
      element: <NotFound />
    },
    {
      path: '*',
      element: <Navigate to="/404" replace />
    }
  ];

  const routes = useRoutes([...dashboardRoutes, ...publicRoutes]);

  return routes;
}
