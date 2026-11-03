import type { Meta, StoryObj } from "../../kc.gen";
import { createKcPageStory } from "../KcPageStory";

const { KcPageStory } = createKcPageStory({ pageId: "webauthn-register.ftl" });

const meta = {
    title: "login/webauthn-register.ftl",
    component: KcPageStory
} satisfies Meta<typeof KcPageStory>;

export default meta;

type Story = StoryObj<typeof meta>;

export const FromAccountConsole: Story = {
    render: () => <KcPageStory kcContext={{ isSetRetry: false, isAppInitiatedAction: true }} />
};

export const WithError: Story = {
    render: () => (
        <KcPageStory
            kcContext={{
                isSetRetry: true,
                isAppInitiatedAction: true,
                message: { type: "error", summary: "Failed to register your Passkey." }
            }}
        />
    )
};
