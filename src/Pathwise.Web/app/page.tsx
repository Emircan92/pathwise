import { BackendStatus } from "@/components/backend-status";

export default function Home() {
  return (
    <main className="min-h-screen bg-background px-6 py-16 sm:px-10 sm:py-24">
      <section className="mx-auto w-full max-w-4xl">
        <div className="max-w-3xl space-y-10">
          <div className="space-y-4">
            <p className="text-sm font-semibold uppercase tracking-[0.22em] text-primary">
              Pathwise
            </p>
            <h1 className="text-balance text-4xl font-semibold tracking-tight sm:text-5xl">
              Turn every jungle game into a clearer next step.
            </h1>
            <p className="max-w-2xl text-pretty text-lg leading-8 text-muted-foreground sm:text-xl">
              Pathwise will reconstruct your matches, surface the periods worth
              reviewing, and connect each takeaway to the evidence behind it.
            </p>
          </div>

          <BackendStatus />
        </div>
      </section>
    </main>
  );
}
