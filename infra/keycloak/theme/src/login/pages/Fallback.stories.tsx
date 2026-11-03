// Pages that are not ejected still render through our Template and main.css.
// These stories cover the ones the integration tests reach.
import type { Meta, StoryObj } from "../../kc.gen";
import { createKcPageStory } from "../KcPageStory";

const { KcPageStory: ErrorPage } = createKcPageStory({ pageId: "error.ftl" });
const { KcPageStory: UpdatePasswordPage } = createKcPageStory({ pageId: "login-update-password.ftl" });

const meta = {
    title: "login/fallback pages"
} satisfies Meta;

export default meta;

type Story = StoryObj<typeof meta>;

export const ProtocolError: Story = {
    render: () => <ErrorPage kcContext={{ message: { type: "error", summary: "Invalid parameter: redirect_uri" } }} />
};

export const RequiredPasswordUpdate: Story = {
    render: () => <UpdatePasswordPage />
};
