import type { Meta, StoryObj } from "../../kc.gen";
import { createKcPageStory } from "../KcPageStory";

const { KcPageStory } = createKcPageStory({ pageId: "login.ftl" });

const meta = {
    title: "login/login.ftl",
    component: KcPageStory
} satisfies Meta<typeof KcPageStory>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: () => <KcPageStory />
};

export const WithInvalidCredentials: Story = {
    render: () => (
        <KcPageStory
            kcContext={{
                login: { username: "visitor" },
                messagesPerField: {
                    existsError: (fieldName: string, ...otherFieldNames: string[]) =>
                        [fieldName, ...otherFieldNames].some(name => name === "username" || name === "password"),
                    get: (fieldName: string) => (fieldName === "username" || fieldName === "password" ? "Invalid username or password." : "")
                }
            }}
        />
    )
};

export const WithPasskeys: Story = {
    render: () => (
        <KcPageStory
            kcContext={{
                url: { loginAction: "/mock-login-action" },
                enableWebAuthnConditionalUI: true
            }}
        />
    )
};

export const WithPasskeyError: Story = {
    render: () => (
        <KcPageStory
            kcContext={{
                url: { loginAction: "/mock-login-action" },
                enableWebAuthnConditionalUI: true,
                message: { type: "error", summary: "Failed to authenticate by the Passkey." }
            }}
        />
    )
};

export const WithLongRealmName: Story = {
    render: () => <KcPageStory kcContext={{ realm: { displayName: "City Picks — Own Your Identity" } }} />
};
