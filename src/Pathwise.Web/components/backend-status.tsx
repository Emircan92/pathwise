"use client";

import { useEffect, useState } from "react";

import { Button } from "@/components/ui/button";

type Availability = "checking" | "available" | "unavailable";

const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5100";

async function getBackendAvailability(): Promise<Availability> {
  try {
    const response = await fetch(`${apiBaseUrl}/health`, {
      cache: "no-store",
    });

    return response.ok ? "available" : "unavailable";
  } catch {
    return "unavailable";
  }
}

export function BackendStatus() {
  const [availability, setAvailability] = useState<Availability>("checking");

  useEffect(() => {
    let isCurrent = true;

    void getBackendAvailability().then((result) => {
      if (isCurrent) {
        setAvailability(result);
      }
    });

    return () => {
      isCurrent = false;
    };
  }, []);

  const isAvailable = availability === "available";
  const isChecking = availability === "checking";

  function retryBackendCheck() {
    setAvailability("checking");
    void getBackendAvailability().then(setAvailability);
  }

  return (
    <div className="flex flex-col gap-4 rounded-2xl border bg-card/80 p-5 shadow-sm backdrop-blur sm:flex-row sm:items-center sm:justify-between">
      <div className="flex items-center gap-3">
        <span
          aria-hidden="true"
          className={`size-2.5 rounded-full ${
            isChecking
              ? "animate-pulse bg-amber-500"
              : isAvailable
                ? "bg-emerald-500"
                : "bg-red-500"
          }`}
        />
        <div>
          <p className="font-medium">
            {isChecking
              ? "Checking the Pathwise API…"
              : isAvailable
                ? "Pathwise API is available"
                : "Pathwise API is unavailable"}
          </p>
          <p className="text-sm text-muted-foreground">
            {isAvailable
              ? "Frontend and backend are connected."
              : "Start the backend on localhost:5100 to connect."}
          </p>
        </div>
      </div>

      {!isAvailable && !isChecking ? (
        <Button variant="outline" onClick={retryBackendCheck}>
          Check again
        </Button>
      ) : null}
    </div>
  );
}
