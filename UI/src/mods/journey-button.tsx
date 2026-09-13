import { bindTrigger, bindValue, useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import { getModule } from "cs2/modding";
import startIcon from "images/journey-start.svg";
import endIcon from "images/journey-end.svg";
import closeIcon from "images/journey-close.svg";

const kGroup = "skylinesMaps";

const mode$ = bindValue<number>(kGroup, "journeyButton", 0);
const triggerJourneyAction = bindTrigger(kGroup, "journeyAction");

const kSectionPath =
    "game-ui/game/components/selected-info-panel/selected-info-sections/shared-sections/actions-section/";

const kCloseIcon = closeIcon;

enum ButtonMode {
    Hidden = 0,
    Start = 1,
    Cancel = 2,
    End = 3,
    Clear = 4,
}

interface ButtonContent {
    icon: string;
    tinted: boolean;
    titleId: string;
    title: string;
    descriptionId: string;
    description: string;
}

const kContent: Partial<Record<ButtonMode, ButtonContent>> = {
    [ButtonMode.Start]: {
        icon: startIcon,
        tinted: false,
        titleId: "SkylinesMaps.JourneyPlanner.START_TITLE",
        title: "Plan a journey",
        descriptionId: "SkylinesMaps.JourneyPlanner.START_DESCRIPTION",
        description: "Start a journey from here, then select the building or road you want to go to.",
    },
    [ButtonMode.Cancel]: {
        icon: kCloseIcon,
        tinted: true,
        titleId: "SkylinesMaps.JourneyPlanner.CANCEL_TITLE",
        title: "Cancel journey",
        descriptionId: "SkylinesMaps.JourneyPlanner.CANCEL_DESCRIPTION",
        description: "Stop choosing a destination for this journey.",
    },
    [ButtonMode.End]: {
        icon: endIcon,
        tinted: false,
        titleId: "SkylinesMaps.JourneyPlanner.END_TITLE",
        title: "End journey here",
        descriptionId: "SkylinesMaps.JourneyPlanner.END_DESCRIPTION",
        description: "Find the best driving route to here, based on travel time and live traffic.",
    },
    [ButtonMode.Clear]: {
        icon: kCloseIcon,
        tinted: true,
        titleId: "SkylinesMaps.JourneyPlanner.CLEAR_TITLE",
        title: "Clear route",
        descriptionId: "SkylinesMaps.JourneyPlanner.CLEAR_DESCRIPTION",
        description: "Remove this journey's route from the map.",
    },
};

interface GameUi {
    IconButton: any;
    DescriptionTooltip: any;
    buttonTheme: any;
    sectionClasses: any;
    cancelClick: any;
}

let cachedGameUi: GameUi | null = null;

const getGameUi = (): GameUi | null => {
    if (cachedGameUi) {
        return cachedGameUi;
    }

    const IconButton = getModule("game-ui/common/input/button/icon-button.tsx", "IconButton");
    if (!IconButton) {
        return null;
    }

    cachedGameUi = {
        IconButton,
        DescriptionTooltip: getModule(
            "game-ui/common/tooltip/description-tooltip/description-tooltip.tsx",
            "DescriptionTooltip"
        ),
        buttonTheme: getModule(`${kSectionPath}action-button.module.scss`, "classes"),
        sectionClasses: getModule(`${kSectionPath}actions-section.module.scss`, "classes"),
        cancelClick: getModule("game-ui/common/utils/cancel-click.ts", "cancelClick"),
    };

    return cachedGameUi;
};

export const JourneyButton = () => {
    const mode = useValue(mode$) as ButtonMode;
    const { translate } = useLocalization();

    const content = kContent[mode];
    const ui = getGameUi();

    if (!content || !ui) {
        return null;
    }

    const title = translate(content.titleId, content.title) ?? content.title;
    const description = translate(content.descriptionId, content.description) ?? content.description;

    const button = (
        <ui.IconButton
            disableHint
            src={content.icon}
            tinted={content.tinted}
            selected={mode === ButtonMode.Cancel}
            theme={ui.buttonTheme}
            className={ui.sectionClasses?.button}
            onSelect={triggerJourneyAction}
            onClick={ui.cancelClick}
        />
    );

    if (!ui.DescriptionTooltip) {
        return button;
    }

    return (
        <ui.DescriptionTooltip title={title} description={description}>
            {button}
        </ui.DescriptionTooltip>
    );
};
