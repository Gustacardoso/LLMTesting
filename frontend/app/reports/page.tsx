"use client";

import { API } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export default function ReportsPage() {
  return (
    <>
      <h1 className="text-2xl font-bold">Reports</h1>
      <Card>
        <CardHeader><CardTitle>Quality report (PDF)</CardTitle></CardHeader>
        <CardContent className="space-y-3 text-sm">
          <p>Executive summary, approved cases, failed cases, main risks and improvement recommendations.</p>
          <Button asChild><a href={`${API}/api/reports/pdf`} target="_blank" rel="noreferrer" data-testid="download-report">Download PDF</a></Button>
        </CardContent>
      </Card>
    </>
  );
}
