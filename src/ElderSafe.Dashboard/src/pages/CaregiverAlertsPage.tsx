import { Bell, ChartColumn } from "lucide-react";
import { Tabs, TabsList, TabsPanel, TabsTab } from "@/components/ui/tabs";
import { AlertsPanel } from "@/components/alerts/AlertsPanel";
import { SummaryPanel } from "@/components/alerts/SummaryPanel";

/**
 * The caregiver's alerts page: live alerts in one tab, a counts-over-time
 * summary in the other (REQ-FALL-02, REQ-FALL-07, REQ-FALL-08, REQ-FALL-09).
 *
 * Both panels are rendered with `keepMounted`, so switching tabs never tears
 * down the Alerts tab's live SignalR connection or restarts the Summary
 * tab's fetch — each panel keeps whatever state it already had.
 */
export function CaregiverAlertsPage() {
  return (
    <Tabs defaultValue="alerts">
      <TabsList className="h-10 w-fit gap-1 rounded-full bg-secondary/70 p-1">
        <TabsTab value="alerts" className="gap-1.5 rounded-full px-4 data-active:shadow-sm">
          <Bell className="size-4" aria-hidden="true" />
          Alerts
        </TabsTab>
        <TabsTab value="summary" className="gap-1.5 rounded-full px-4 data-active:shadow-sm">
          <ChartColumn className="size-4" aria-hidden="true" />
          Summary
        </TabsTab>
      </TabsList>

      <TabsPanel value="alerts" keepMounted>
        <AlertsPanel />
      </TabsPanel>

      <TabsPanel value="summary" keepMounted>
        <SummaryPanel />
      </TabsPanel>
    </Tabs>
  );
}
