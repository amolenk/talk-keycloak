import type { Meta, StoryObj } from "../../kc.gen";
import { createKcPageStory } from "../KcPageStory";

const { KcPageStory } = createKcPageStory({ pageId: "register.ftl" });

const meta = {
    title: "login/register.ftl",
    component: KcPageStory
} satisfies Meta<typeof KcPageStory>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: () => <KcPageStory />
};

export const WithFieldErrors: Story = {
    render: () => (
        <KcPageStory
            kcContext={{
                profile: {
                    attributesByName: {
                        username: { value: "" },
                        email: { value: "invalid-email" }
                    }
                },
                messagesPerField: {
                    existsError: (fieldName: string) => ["username", "email"].includes(fieldName),
                    get: (fieldName: string) => {
                        if (fieldName === "username") return "Please specify username.";
                        if (fieldName === "email") return "Invalid email address.";
                        return "";
                    }
                }
            }}
        />
    )
};
