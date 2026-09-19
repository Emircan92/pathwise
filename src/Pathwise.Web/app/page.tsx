import { BackendStatus } from "@/components/backend-status";

export default function Home() {
  return (
    <main className="relative flex min-h-screen items-center overflow-hidden bg-background px-6 py-16 sm:px-10">
      <div className="absolute inset-0 -z-10 bg-[radial-gradient(circle_at_top_left,oklch(0.9_0.06_155/.45),transparent_42%)]" />

      <section className="mx-auto w-full max-w-5xl">
        <div className="max-w-3xl space-y-8">
          <div className="space-y-4">
            <p className="text-sm font-semibold uppercase tracking-[0.24em] text-emerald-700">
              Pathwise
            </p>
            <h1 className="text-balance text-5xl font-semibold tracking-tight sm:text-7xl">
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
