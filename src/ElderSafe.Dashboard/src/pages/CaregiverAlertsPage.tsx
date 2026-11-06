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
      <TabsList>
        <TabsTab value="alerts">Alerts</TabsTab>
        <TabsTab value="summary">Summary</TabsTab>
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
