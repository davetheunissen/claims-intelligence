"use client";

import { useEffect, useRef } from "react";
import { useRouter } from "next/navigation";
import { makeStyles, tokens } from "@fluentui/react-components";
import type { JourneyMode } from "../../components/journey/JourneySection";
import { StickyStepper } from "../../components/journey/StickyStepper";
import { TopBar } from "../../components/TopBar";
import { useClaimStore } from "../../store/claimStore";
import { Step1Documents } from "../../components/journey/steps/Step1Documents";
import { Step2Entities } from "../../components/journey/steps/Step2Entities";
import { Step3Coverage } from "../../components/journey/steps/Step3Coverage";
import { Step4Fraud } from "../../components/journey/steps/Step4Fraud";
import { Step5Review } from "../../components/journey/steps/Step5Review";
import { Step6Recommendation } from "../../components/journey/steps/Step6Recommendation";
import { Step7Email } from "../../components/journey/steps/Step7Email";

const useStyles = makeStyles({
  shell: {
    display: "flex",
    flexDirection: "column",
    height: "100vh",
    overflow: "hidden",
  },
  scrollArea: {
    flex: 1,
    overflowY: "auto",
  },
  journeyRoot: {
    minHeight: "100%",
    backgroundImage:
      "radial-gradient(ellipse at top, rgba(43,197,180,0.06), transparent 60%), radial-gradient(ellipse at bottom right, rgba(35,105,186,0.08), transparent 60%)",
    backgroundColor: tokens.colorNeutralBackground2,
  },
  bottomSpacer: {
    height: "40vh",
  },
});

function scrollToSection(step: number) {
  const el = document.getElementById(`section-${step}`);
  if (el) {
    el.scrollIntoView({ behavior: "smooth", block: "start" });
  }
}

const SECTION_COMPONENTS = [
  Step1Documents,
  Step2Entities,
  Step3Coverage,
  Step4Fraud,
  Step5Review,
  Step6Recommendation,
  Step7Email,
] as const;

export default function JourneyPage() {
  const styles = useStyles();
  const router = useRouter();
  const claimId = useClaimStore((s) => s.claimId);
  const currentStep = useClaimStore((s) => s.currentStep);
  const completed = useClaimStore((s) => s.completed);
  const markComplete = useClaimStore((s) => s.markComplete);
  const goTo = useClaimStore((s) => s.goTo);
  const lastAutoScrolledStep = useRef<number>(0);

  // Guard: redirect to home if no claim loaded
  useEffect(() => {
    if (!claimId) {
      router.replace("/");
    }
  }, [claimId, router]);

  useEffect(() => {
    if (lastAutoScrolledStep.current !== currentStep) {
      lastAutoScrolledStep.current = currentStep;
      const t = setTimeout(() => scrollToSection(currentStep), 480);
      return () => clearTimeout(t);
    }
  }, [currentStep]);

  const handleStepClick = (step: number) => {
    goTo(step);
    scrollToSection(step);
  };

  if (!claimId) return null;

  return (
    <div className={styles.shell}>
      <TopBar />
      <div className={styles.scrollArea}>
        <div className={styles.journeyRoot}>
          <StickyStepper onStepClick={handleStepClick} />
          {SECTION_COMPONENTS.map((Component, idx) => {
            const step = idx + 1;
            const isDone = completed.has(step);
            const isActive = step === currentStep;
            const mode: JourneyMode = isActive ? "active" : isDone ? "done" : "locked";
            return (
              <Component
                key={step}
                mode={mode}
                onNext={() => markComplete(step)}
                onEdit={() => handleStepClick(step)}
              />
            );
          })}
          <div className={styles.bottomSpacer} />
        </div>
      </div>
    </div>
  );
}
